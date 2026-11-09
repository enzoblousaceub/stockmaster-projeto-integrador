using System.Security.Claims;
using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    [Route("api/auth")]
    public class AuthController : ApiControllerBase
    {
        private readonly AppDbContext _db;
        private readonly Pbkdf2PasswordHasher _hasher;
        private readonly ActivationCodes _codes;
        private readonly IpFailureLimiter _ipLimiter;
        private readonly AuthThrottleService _throttle;
        private readonly AuditService _audit;

        public AuthController(AppDbContext db, Pbkdf2PasswordHasher hasher, ActivationCodes codes,
            IpFailureLimiter ipLimiter, AuthThrottleService throttle, AuditService audit)
        {
            _db = db; _hasher = hasher; _codes = codes; _ipLimiter = ipLimiter; _throttle = throttle; _audit = audit;
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto, CancellationToken ct)
        {
            var usuario = UsuarioRules.Normalize(dto.Usuario);

            if (_ipLimiter.BlockedSeconds(ClientIp) is int ipWait) return RateLimited(ipWait);
            if (await _throttle.LockedSecondsAsync(AuthThrottleService.Login, usuario, ct) is int wait)
            {
                await _audit.LogAsync("login_bloqueado", "falha", null, usuario);
                return Locked(wait);
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Usuario == usuario, ct);
            var ok = false;
            if (user is { IsActive: true, PasswordHash: not null })
                ok = _hasher.Verify(user.PasswordHash, dto.Senha);
            else
                _hasher.VerifyDummy(dto.Senha);   // mesmo custo de tempo: nao revela se o usuario existe

            if (!ok || user is null)
            {
                await _throttle.RegisterFailureAsync(AuthThrottleService.Login, usuario, ct);
                _ipLimiter.RecordFailure(ClientIp);
                await _audit.LogAsync("login_falha", "falha", null, usuario);
                return Fail(401, "invalid_credentials", "Usuario ou senha invalidos");
            }

            await _throttle.ResetAsync(AuthThrottleService.Login, usuario, ct);
            if (_hasher.NeedsRehash(user.PasswordHash!)) user.PasswordHash = _hasher.Hash(dto.Senha);
            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var principal = AuthSetup.BuildPrincipal(user);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true });
            HttpContext.User = principal;
            AuthSetup.IssueCsrfToken(HttpContext);
            await _audit.LogAsync("login_ok", "ok", usuario, usuario);
            return Ok(new MeDto(user.Usuario, user.Role));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(CancellationToken ct)
        {
            var id = CurrentUserId();
            // avanca o stamp: encerra TAMBEM as outras sessoes da pessoa (todos os dispositivos)
            await _db.Users.Where(u => u.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, Guid.NewGuid().ToString("N")), ct);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            AuthSetup.ClearCsrfCookies(HttpContext);
            await _audit.LogAsync("logout", "ok", ActorName, ActorName);
            return NoContent();
        }

        [HttpGet("me")]
        public IActionResult Me()
        {
            AuthSetup.IssueCsrfToken(HttpContext);
            return Ok(new MeDto(User.Identity!.Name!, User.FindFirstValue(ClaimTypes.Role)!));
        }

        [HttpPost("senha")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto, CancellationToken ct)
        {
            var usuario = ActorName!;
            if (dto.NovaSenha.Length < PasswordRules.Min || dto.NovaSenha.Length > PasswordRules.Max)
                return PasswordError("password_too_short");
            if (_ipLimiter.BlockedSeconds(ClientIp) is int ipWait) return RateLimited(ipWait);
            if (await _throttle.LockedSecondsAsync(AuthThrottleService.Login, usuario, ct) is int wait) return Locked(wait);

            var id = CurrentUserId();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user?.PasswordHash is null) return Fail(401, "unauthenticated", "Nao autenticado");

            if (!_hasher.Verify(user.PasswordHash, dto.SenhaAtual))
            {
                await _throttle.RegisterFailureAsync(AuthThrottleService.Login, usuario, ct);
                _ipLimiter.RecordFailure(ClientIp);
                await _audit.LogAsync("senha_alterada", "falha", usuario, usuario, "senha atual incorreta");
                // 400 (nao 401): o front nao deve deslogar a pessoa por digitar a senha atual errada
                return Fail(400, "current_password_invalid", "Senha atual incorreta");
            }

            if (PasswordPolicy.Validate(dto.NovaSenha, usuario) is string policy) return PasswordError(policy);

            user.PasswordHash = _hasher.Hash(dto.NovaSenha);
            user.SecurityStamp = Guid.NewGuid().ToString("N");   // derruba as outras sessoes
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedBy = usuario;
            await _db.SaveChangesAsync(ct);
            await _throttle.ResetAsync(AuthThrottleService.Login, usuario, ct);

            // reemite a sessao desta pessoa com o stamp novo (continua logada)
            var principal = AuthSetup.BuildPrincipal(user, AuthSetup.AbsoluteOf(User));
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true });
            HttpContext.User = principal;
            AuthSetup.IssueCsrfToken(HttpContext);
            await _audit.LogAsync("senha_alterada", "ok", usuario, usuario);
            return NoContent();
        }

        [AllowAnonymous]
        [HttpPost("ativar")]
        public async Task<IActionResult> Activate(AtivarDto dto, CancellationToken ct)
        {
            var usuario = UsuarioRules.Normalize(dto.Usuario);

            // 1) formato
            var code = ActivationCodes.Normalize(dto.Codigo);
            if (code is null)
                return Fail(400, "validation_error", "Dados invalidos", errors: new Dictionary<string, string>
                    { ["codigo"] = "O codigo de ativacao tem 12 caracteres (letras e numeros)." });
            if (dto.NovaSenha.Length < PasswordRules.Min || dto.NovaSenha.Length > PasswordRules.Max)
                return PasswordError("password_too_short");
            if (!_codes.IsConfigured)
                return Fail(503, "service_misconfigured", "Servico indisponivel", "Ativacao indisponivel no momento.");

            // 2) bloqueio (por usuario, existente ou nao, e por IP)
            if (_ipLimiter.BlockedSeconds(ClientIp) is int ipWait) return RateLimited(ipWait);
            if (await _throttle.LockedSecondsAsync(AuthThrottleService.Activate, usuario, ct) is int wait)
            {
                await _audit.LogAsync("ativacao_bloqueada", "falha", null, usuario);
                return Locked(wait);
            }

            // 3) codigo (comparacao em tempo constante; usuario inexistente = mesma comparacao com valor falso)
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Usuario == usuario, ct);
            var matches = _codes.Matches(user?.ActivationCodeHash, usuario, code);
            if (user is null || !user.IsActive || !matches)
            {
                await _throttle.RegisterFailureAsync(AuthThrottleService.Activate, usuario, ct);
                _ipLimiter.RecordFailure(ClientIp);
                await _audit.LogAsync("ativacao_falha", "falha", null, usuario);
                return Fail(400, "activation_code_invalid", "Codigo de ativacao invalido");
            }

            // codigo correto: agora e seguro distinguir usado/expirado
            if (user.ActivationUsedAt is not null || user.PasswordHash is not null)
                return Fail(409, "activation_code_used", "Codigo de ativacao ja utilizado");
            if (user.ActivationExpiresAt is null || user.ActivationExpiresAt < DateTime.UtcNow)
                return Fail(410, "activation_code_expired", "Codigo de ativacao expirado", "Peca um novo codigo ao administrador.");

            // 4) politica da senha: NAO consome o codigo nem conta como falha
            if (PasswordPolicy.Validate(dto.NovaSenha, usuario) is string policy) return PasswordError(policy);

            // 5) consome (atomico: so um request vence)
            var hash = _hasher.Hash(dto.NovaSenha);
            var now = DateTime.UtcNow;
            var stamp = Guid.NewGuid().ToString("N");
            var affected = await _db.Users
                .Where(u => u.Id == user.Id && u.ActivationUsedAt == null && u.PasswordHash == null && u.IsActive)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PasswordHash, hash)
                    .SetProperty(u => u.ActivationUsedAt, now)
                    .SetProperty(u => u.SecurityStamp, stamp)
                    .SetProperty(u => u.UpdatedAt, now)
                    .SetProperty(u => u.UpdatedBy, usuario), ct);
            if (affected == 0) return Fail(409, "activation_code_used", "Codigo de ativacao ja utilizado");

            await _throttle.ResetAsync(AuthThrottleService.Activate, usuario, ct);
            await _audit.LogAsync("ativacao_ok", "ok", usuario, usuario);
            return NoContent();
        }

        private ObjectResult PasswordError(string code) => Fail(400, code,
            code == "password_too_short" ? "Senha muito curta" : "Senha fraca",
            errors: new Dictionary<string, string> { ["novaSenha"] = PasswordPolicy.Message(code) });

        private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
