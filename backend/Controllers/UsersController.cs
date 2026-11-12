using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;

namespace backend.Controllers
{
    [Route("api/users")]
    [Authorize(Policy = AuthSetup.AdminPolicy)]
    public class UsersController : ApiControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ActivationCodes _codes;
        private readonly AuthThrottleService _throttle;
        private readonly AuditService _audit;
        private readonly InviteMailer _mailer;

        public UsersController(AppDbContext db, ActivationCodes codes, AuthThrottleService throttle, AuditService audit, InviteMailer mailer)
        {
            _db = db; _codes = codes; _throttle = throttle; _audit = audit; _mailer = mailer;
        }

        /// <summary>Envia o convite. Com sucesso o codigo NAO volta na resposta (so quem recebe o e-mail o conhece); com falha, volta para o Admin repassar.</summary>
        private async Task<(string? Code, bool Sent)> InviteAsync(AppUser user, string code, CancellationToken ct)
        {
            var sent = await _mailer.SendAsync(user, code, Request, ct);
            await _audit.LogAsync("convite_email", sent ? "ok" : "falha", ActorName, user.Email);
            return (sent ? null : code, sent);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDto>>> List(CancellationToken ct)
        {
            var users = await _db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
            var now = DateTime.UtcNow;
            var locks = await _db.AuthThrottles.AsNoTracking()
                .Where(t => t.LockedUntil > now)
                .GroupBy(t => t.Key)
                .Select(g => new { Key = g.Key, Until = g.Max(t => t.LockedUntil) })
                .ToDictionaryAsync(x => x.Key, x => x.Until, ct);
            return Ok(users.Select(u => UserDto.From(u, locks.GetValueOrDefault(u.Email))));
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateUserDto dto, CancellationToken ct)
        {
            if (!Roles.IsValid(dto.Perfil)) return InvalidProfile();
            if (!_codes.IsConfigured) return Misconfigured();

            if (!EmailRules.IsValid(dto.Email)) return InvalidEmail();
            var usuario = EmailRules.Normalize(dto.Email);
            if (await _db.Users.AnyAsync(u => u.Email == usuario, ct)) return Exists();

            var (code, normalized) = NewCode();
            var user = new AppUser
            {
                Email = usuario,
                Role = dto.Perfil,
                CreatedBy = ActorName,
                UpdatedBy = ActorName
            };
            SetActivation(user, usuario, normalized);
            _db.Users.Add(user);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Exists();
            }

            await _audit.LogAsync("usuario_criado", "ok", ActorName, usuario, $"perfil {dto.Perfil}");
            var (shown, sent) = await InviteAsync(user, code, ct);
            return StatusCode(201, UserDto.From(user, null, shown, sent));
        }

        [HttpPatch("{id:int}")]
        public async Task<IActionResult> Update(int id, UpdateUserDto dto, CancellationToken ct)
        {
            if (dto.Perfil is null && dto.Ativo is null && dto.Email is null)
                return Fail(400, "validation_error", "Dados invalidos", "Informe perfil, ativo e/ou email.");
            if (dto.Perfil is not null && !Roles.IsValid(dto.Perfil)) return InvalidProfile();
            if (dto.Email is not null && !EmailRules.IsValid(dto.Email)) return InvalidEmail();

            // serializa as operacoes que podem remover um Admin ativo (evita dois Admins se removendo ao mesmo tempo)
            await using var tx = await BeginAdminLockAsync(ct);
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return NotFoundUser(id);

            var newRole = dto.Perfil ?? user.Role;
            var newActive = dto.Ativo ?? user.IsActive;
            var roleChanged = newRole != user.Role;
            var activeChanged = newActive != user.IsActive;

            var newEmail = dto.Email is null ? user.Email : EmailRules.Normalize(dto.Email);
            var emailChanged = newEmail != user.Email;
            // e-mail so muda enquanto a pessoa ainda nao definiu senha (o codigo e amarrado ao e-mail)
            if (emailChanged && user.PasswordHash is not null)
                return Fail(409, "email_locked", "E-mail bloqueado", "O e-mail so pode ser alterado enquanto a pessoa nao ativou o acesso.");
            if (emailChanged && await _db.Users.AnyAsync(u => u.Id != user.Id && u.Email == newEmail, ct)) return Exists();

            // repetir o mesmo estado e idempotente: 200 sem mudanca
            if (!roleChanged && !activeChanged && !emailChanged) return Ok(UserDto.From(user, await LockedUntil(user.Email, ct)));

            if (user.IsLoginCapableAdmin && (!newActive || newRole != Roles.Admin) && !await HasOtherCapableAdmin(user.Id, ct))
                return LastAdmin();

            if (emailChanged) user.Email = newEmail;   // antes de gerar codigo: o hash do codigo leva o e-mail
            string? code = null;
            if (activeChanged && !newActive)
            {
                user.IsActive = false;
                // codigo pendente deixa de valer (o de quem ja ativou fica, para distinguir "ja usado")
                if (user.ActivationUsedAt is null) { user.ActivationCodeHash = null; user.ActivationExpiresAt = null; }
            }
            else if (activeChanged)
            {
                user.IsActive = true;
                if (user.PasswordHash is null)
                {
                    if (!_codes.IsConfigured) return Misconfigured();
                    var n = NewCode();
                    code = n.Code;
                    SetActivation(user, user.Email, n.Normalized);
                }
            }
            if (emailChanged)
            {
                // o codigo anterior era amarrado ao e-mail antigo: emite outro (se a conta esta ativa)
                if (newActive && code is null)
                {
                    if (!_codes.IsConfigured) return Misconfigured();
                    var n = NewCode();
                    code = n.Code;
                    SetActivation(user, user.Email, n.Normalized);
                }
            }
            user.Role = newRole;
            user.SecurityStamp = Guid.NewGuid().ToString("N");   // sessoes antigas caem
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedBy = ActorName;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            if (roleChanged) await _audit.LogAsync("usuario_perfil_alterado", "ok", ActorName, user.Email, $"perfil {newRole}");
            if (activeChanged) await _audit.LogAsync(newActive ? "usuario_reativado" : "usuario_desativado", "ok", ActorName, user.Email);
            if (emailChanged) await _audit.LogAsync("usuario_email_alterado", "ok", ActorName, user.Email);
            bool? sent = null;
            string? shown = code;
            if (code is not null) (shown, sent) = await InviteAsync(user, code, ct);
            return Ok(UserDto.From(user, await LockedUntil(user.Email, ct), shown, sent));
        }

        [HttpPost("{id:int}/reset")]
        public async Task<IActionResult> Reset(int id, CancellationToken ct)
        {
            await using var tx = await BeginAdminLockAsync(ct);
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return NotFoundUser(id);
            if (!user.IsActive)
                return Fail(409, "user_inactive", "Usuario inativo", "Reative o usuario antes de redefinir a senha.");
            if (!_codes.IsConfigured) return Misconfigured();
            if (user.IsLoginCapableAdmin && !await HasOtherCapableAdmin(user.Id, ct)) return LastAdmin();

            var (code, normalized) = NewCode();
            user.PasswordHash = null;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            SetActivation(user, user.Email, normalized);
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedBy = ActorName;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await _audit.LogAsync("usuario_resetado", "ok", ActorName, user.Email);
            var (shown, sent) = await InviteAsync(user, code, ct);
            return Ok(new { codigoAtivacao = shown, codigoExpiraEm = user.ActivationExpiresAt, emailEnviado = sent });
        }

        [HttpPost("{id:int}/desbloquear")]
        public async Task<IActionResult> Unlock(int id, CancellationToken ct)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return NotFoundUser(id);
            await _throttle.ResetAllAsync(user.Email, ct);
            await _audit.LogAsync("usuario_desbloqueado", "ok", ActorName, user.Email);
            return NoContent();
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginAdminLockAsync(CancellationToken ct)
        {
            var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7001)", ct);
            return tx;
        }

        private (string Code, string Normalized) NewCode()
        {
            var code = ActivationCodes.Generate();
            return (code, ActivationCodes.Normalize(code)!);
        }

        private void SetActivation(AppUser user, string usuario, string normalizedCode)
        {
            user.ActivationCodeHash = _codes.Hash(usuario, normalizedCode);
            user.ActivationExpiresAt = DateTime.UtcNow + ActivationCodes.Lifetime;
            user.ActivationUsedAt = null;
        }

        private Task<bool> HasOtherCapableAdmin(int exceptId, CancellationToken ct) =>
            _db.Users.AnyAsync(u => u.Id != exceptId && u.IsActive && u.PasswordHash != null && u.Role == Roles.Admin, ct);

        private Task<DateTime?> LockedUntil(string usuario, CancellationToken ct) =>
            _db.AuthThrottles.AsNoTracking()
                .Where(t => t.Key == usuario && t.LockedUntil > DateTime.UtcNow)
                .Select(t => t.LockedUntil).OrderByDescending(x => x).FirstOrDefaultAsync(ct);

        private ObjectResult InvalidProfile() => Fail(400, "validation_error", "Dados invalidos",
            errors: new Dictionary<string, string> { ["perfil"] = $"Perfil deve ser exatamente \"{Roles.Admin}\" ou \"{Roles.Viewer}\"." });

        private ObjectResult InvalidEmail() => Fail(400, "validation_error", "Dados invalidos",
            errors: new Dictionary<string, string> { ["email"] = "Informe um e-mail valido." });
        private ObjectResult Exists() => Fail(409, "email_exists", "E-mail ja cadastrado");
        private ObjectResult LastAdmin() => Fail(409, "last_admin", "Ultimo administrador",
            "A operacao deixaria o sistema sem nenhum Admin ativo.");
        private ObjectResult Misconfigured() => Fail(503, "service_misconfigured", "Servico indisponivel",
            "Chave de codigos de ativacao nao configurada no servidor.");
        private ObjectResult NotFoundUser(int id) => Fail(404, "not_found", "Usuario nao encontrado", $"Usuario {id} nao encontrado.");
    }

    [Route("api/audit")]
    [Authorize(Policy = AuthSetup.AdminPolicy)]
    public class AuditController : ApiControllerBase
    {
        private readonly AppDbContext _db;
        public AuditController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<AuditDto>>> List([FromQuery] int limite = 100, CancellationToken ct = default)
        {
            limite = Math.Clamp(limite, 1, 500);
            var rows = await _db.AuditLogs.AsNoTracking()
                .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(limite)
                .Select(a => new AuditDto(a.Id, a.OccurredAt, a.Evento, a.AtorUsuario, a.AlvoUsuario, a.Ip, a.Resultado, a.Detalhe))
                .ToListAsync(ct);
            return Ok(rows);
        }
    }
}
