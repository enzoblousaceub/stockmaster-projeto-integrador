using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

        public UsersController(AppDbContext db, ActivationCodes codes, AuthThrottleService throttle, AuditService audit)
        {
            _db = db; _codes = codes; _throttle = throttle; _audit = audit;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDto>>> List(CancellationToken ct)
        {
            var users = await _db.Users.AsNoTracking().OrderBy(u => u.Usuario).ToListAsync(ct);
            var now = DateTime.UtcNow;
            var locks = await _db.AuthThrottles.AsNoTracking()
                .Where(t => t.Kind == AuthThrottleService.Login && t.LockedUntil > now)
                .ToDictionaryAsync(t => t.Key, t => t.LockedUntil, ct);
            return Ok(users.Select(u => UserDto.From(u, locks.GetValueOrDefault(u.Usuario))));
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateUserDto dto, CancellationToken ct)
        {
            if (!Roles.IsValid(dto.Perfil)) return InvalidProfile();
            if (!_codes.IsConfigured) return Misconfigured();

            var usuario = UsuarioRules.Normalize(dto.Usuario);
            if (await _db.Users.AnyAsync(u => u.Usuario == usuario, ct)) return Exists();

            var (code, normalized) = NewCode();
            var user = new AppUser
            {
                Usuario = usuario,
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
            return StatusCode(201, UserDto.From(user, null, code));
        }

        [HttpPatch("{id:int}")]
        public async Task<IActionResult> Update(int id, UpdateUserDto dto, CancellationToken ct)
        {
            if (dto.Perfil is null && dto.Ativo is null)
                return Fail(400, "validation_error", "Dados invalidos", "Informe perfil e/ou ativo.");
            if (dto.Perfil is not null && !Roles.IsValid(dto.Perfil)) return InvalidProfile();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return NotFoundUser(id);

            var newRole = dto.Perfil ?? user.Role;
            var newActive = dto.Ativo ?? user.IsActive;
            var roleChanged = newRole != user.Role;
            var activeChanged = newActive != user.IsActive;

            // repetir o mesmo estado e idempotente: 200 sem mudanca
            if (!roleChanged && !activeChanged) return Ok(UserDto.From(user, await LockedUntil(user.Usuario, ct)));

            if (user.IsLoginCapableAdmin && (!newActive || newRole != Roles.Admin) && !await HasOtherCapableAdmin(user.Id, ct))
                return LastAdmin();

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
                    SetActivation(user, user.Usuario, n.Normalized);
                }
            }
            user.Role = newRole;
            user.SecurityStamp = Guid.NewGuid().ToString("N");   // sessoes antigas caem
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedBy = ActorName;
            await _db.SaveChangesAsync(ct);

            if (roleChanged) await _audit.LogAsync("usuario_perfil_alterado", "ok", ActorName, user.Usuario, $"perfil {newRole}");
            if (activeChanged) await _audit.LogAsync(newActive ? "usuario_reativado" : "usuario_desativado", "ok", ActorName, user.Usuario);
            return Ok(UserDto.From(user, await LockedUntil(user.Usuario, ct), code));
        }

        [HttpPost("{id:int}/reset")]
        public async Task<IActionResult> Reset(int id, CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return NotFoundUser(id);
            if (!user.IsActive)
                return Fail(409, "user_inactive", "Usuario inativo", "Reative o usuario antes de redefinir a senha.");
            if (!_codes.IsConfigured) return Misconfigured();
            if (user.IsLoginCapableAdmin && !await HasOtherCapableAdmin(user.Id, ct)) return LastAdmin();

            var (code, normalized) = NewCode();
            user.PasswordHash = null;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            SetActivation(user, user.Usuario, normalized);
            user.UpdatedAt = DateTime.UtcNow;
            user.UpdatedBy = ActorName;
            await _db.SaveChangesAsync(ct);

            await _audit.LogAsync("usuario_resetado", "ok", ActorName, user.Usuario);
            return Ok(new { codigoAtivacao = code, codigoExpiraEm = user.ActivationExpiresAt });
        }

        [HttpPost("{id:int}/desbloquear")]
        public async Task<IActionResult> Unlock(int id, CancellationToken ct)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return NotFoundUser(id);
            await _throttle.ResetAllAsync(user.Usuario, ct);
            await _audit.LogAsync("usuario_desbloqueado", "ok", ActorName, user.Usuario);
            return NoContent();
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
                .Where(t => t.Kind == AuthThrottleService.Login && t.Key == usuario && t.LockedUntil > DateTime.UtcNow)
                .Select(t => t.LockedUntil).FirstOrDefaultAsync(ct);

        private ObjectResult InvalidProfile() => Fail(400, "validation_error", "Dados invalidos",
            errors: new Dictionary<string, string> { ["perfil"] = $"Perfil deve ser exatamente \"{Roles.Admin}\" ou \"{Roles.Viewer}\"." });

        private ObjectResult Exists() => Fail(409, "usuario_exists", "Usuario ja existe");
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
