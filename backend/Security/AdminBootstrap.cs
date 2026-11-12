using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Security
{
    /// <summary>
    /// Cria o primeiro Admin (ou recupera um Admin quando nao existe nenhum ativo) a partir de
    /// Auth__AdminEmail / Auth__AdminSenha. Nunca ha senha padrao: sem as variaveis, nao cria e avisa.
    /// A senha so e lida aqui; depois pode sair do ambiente.
    /// </summary>
    public static class AdminBootstrap
    {
        public static async Task RunAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var config = sp.GetRequiredService<IConfiguration>();
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AdminBootstrap");

            // migracao: contas antigas guardavam um "usuario" (sem @) no lugar do e-mail
            await FixLegacyAsync(db, config, log);

            // se a tabela de usuarios ja tem um Admin capaz de entrar, nada a fazer
            if (await db.Users.AnyAsync(u => u.IsActive && u.PasswordHash != null && u.Role == Roles.Admin)) return;

            var usuarioRaw = config["Auth:AdminEmail"];
            var senha = config["Auth:AdminSenha"];
            if (string.IsNullOrWhiteSpace(usuarioRaw) || string.IsNullOrEmpty(senha))
            {
                log.LogError("Nenhum Admin ativo e Auth__AdminEmail/Auth__AdminSenha nao definidos: nenhum Admin foi criado. Defina as variaveis e reinicie.");
                return;
            }
            if (!EmailRules.IsValid(usuarioRaw))
            {
                log.LogError("Auth__AdminEmail invalido (use um e-mail): Admin nao criado.");
                return;
            }
            var usuario = EmailRules.Normalize(usuarioRaw);
            var policyError = PasswordPolicy.Validate(senha, usuario);
            if (policyError is not null)
            {
                log.LogError("Auth__AdminSenha rejeitada pela politica de senha ({Code}): Admin nao criado.", policyError);
                return;
            }

            var hasher = sp.GetRequiredService<Pbkdf2PasswordHasher>();
            var audit = sp.GetRequiredService<AuditService>();
            var anyUser = await db.Users.AnyAsync();
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == usuario);

            if (existing is null)
            {
                db.Users.Add(new AppUser
                {
                    Email = usuario,
                    Role = Roles.Admin,
                    PasswordHash = await hasher.HashAsync(senha),
                    CreatedBy = "bootstrap",
                    UpdatedBy = "bootstrap"
                });
                await db.SaveChangesAsync();
            }
            else
            {
                existing.Role = Roles.Admin;
                existing.IsActive = true;
                existing.PasswordHash = await hasher.HashAsync(senha);
                existing.SecurityStamp = Guid.NewGuid().ToString("N");
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = "bootstrap";
                await db.SaveChangesAsync();
                await sp.GetRequiredService<AuthThrottleService>().ResetAllAsync(usuario);
            }

            await audit.LogAsync(anyUser ? "admin_recuperado" : "admin_bootstrap", "ok", "bootstrap", usuario);
            log.LogInformation("Admin '{Email}' {Acao}.", usuario, anyUser ? "recuperado" : "criado");
        }

        /// <summary>
        /// Contas anteriores ao login por e-mail tem um nome (sem @) no campo Email. O Admin sem e-mail recebe
        /// Auth__AdminEmail (mesma conta e mesma senha). Qualquer outra conta sem e-mail valido nao consegue entrar
        /// e e listada no log para o Admin recria-la.
        /// </summary>
        private static async Task FixLegacyAsync(AppDbContext db, IConfiguration config, ILogger log)
        {
            var legacy = await db.Users.Where(u => !u.Email.Contains("@")).OrderBy(u => u.Id).ToListAsync();
            if (legacy.Count == 0) return;

            var adminEmail = config["Auth:AdminEmail"];
            if (EmailRules.IsValid(adminEmail))
            {
                var email = EmailRules.Normalize(adminEmail!);
                var admin = legacy.FirstOrDefault(u => u.Role == Roles.Admin);
                if (admin is not null && !await db.Users.AnyAsync(u => u.Email == email))
                {
                    log.LogWarning("Conta '{Antigo}' migrada para o e-mail {Email}.", admin.Email, email);
                    admin.Email = email;
                    admin.SecurityStamp = Guid.NewGuid().ToString("N");
                    admin.UpdatedAt = DateTime.UtcNow;
                    admin.UpdatedBy = "bootstrap";
                    await db.SaveChangesAsync();
                    legacy.Remove(admin);
                }
            }
            foreach (var u in legacy)
                log.LogError("Conta '{Antigo}' ({Perfil}) nao tem e-mail e NAO consegue entrar. Desative-a e crie de novo com o e-mail da pessoa (no Admin sem e-mail, defina Auth__AdminEmail).", u.Email, u.Role);
        }
    }
}
