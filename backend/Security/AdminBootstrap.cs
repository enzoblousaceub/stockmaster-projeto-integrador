using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Security
{
    /// <summary>
    /// Cria o primeiro Admin (ou recupera um Admin quando nao existe nenhum ativo) a partir de
    /// Auth__AdminUsuario / Auth__AdminSenha. Nunca ha senha padrao: sem as variaveis, nao cria e avisa.
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

            // se a tabela de usuarios ja tem um Admin capaz de entrar, nada a fazer
            if (await db.Users.AnyAsync(u => u.IsActive && u.PasswordHash != null && u.Role == Roles.Admin)) return;

            var usuarioRaw = config["Auth:AdminUsuario"];
            var senha = config["Auth:AdminSenha"];
            if (string.IsNullOrWhiteSpace(usuarioRaw) || string.IsNullOrEmpty(senha))
            {
                log.LogWarning("Nenhum Admin ativo e Auth__AdminUsuario/Auth__AdminSenha nao definidos: nenhum Admin foi criado. Defina as variaveis e reinicie.");
                return;
            }
            if (!UsuarioRules.IsValid(usuarioRaw))
            {
                log.LogError("Auth__AdminUsuario invalido (use 3-32 caracteres [a-z0-9._-]): Admin nao criado.");
                return;
            }
            var usuario = UsuarioRules.Normalize(usuarioRaw);
            var policyError = PasswordPolicy.Validate(senha, usuario);
            if (policyError is not null)
            {
                log.LogError("Auth__AdminSenha rejeitada pela politica de senha ({Code}): Admin nao criado.", policyError);
                return;
            }

            var hasher = sp.GetRequiredService<Pbkdf2PasswordHasher>();
            var audit = sp.GetRequiredService<AuditService>();
            var anyUser = await db.Users.AnyAsync();
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Usuario == usuario);

            if (existing is null)
            {
                db.Users.Add(new AppUser
                {
                    Usuario = usuario,
                    Role = Roles.Admin,
                    PasswordHash = hasher.Hash(senha),
                    CreatedBy = "bootstrap",
                    UpdatedBy = "bootstrap"
                });
                await db.SaveChangesAsync();
            }
            else
            {
                existing.Role = Roles.Admin;
                existing.IsActive = true;
                existing.PasswordHash = hasher.Hash(senha);
                existing.SecurityStamp = Guid.NewGuid().ToString("N");
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = "bootstrap";
                await db.SaveChangesAsync();
                await sp.GetRequiredService<AuthThrottleService>().ResetAllAsync(usuario);
            }

            await audit.LogAsync(anyUser ? "admin_recuperado" : "admin_bootstrap", "ok", "bootstrap", usuario);
            log.LogInformation("Admin '{Usuario}' {Acao}.", usuario, anyUser ? "recuperado" : "criado");
        }
    }
}
