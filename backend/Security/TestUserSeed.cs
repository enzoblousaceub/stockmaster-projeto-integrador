using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Security
{
    /// <summary>
    /// Cria um usuario de teste para desenvolvimento a partir de Seed__TestEmail / Seed__TestSenha
    /// (perfil opcional em Seed__TestPerfil: "Visualizador" (padrao) ou "Admin").
    /// So roda com Seed:Enabled (ligado apenas em Development). Sem as variaveis nao faz nada:
    /// nunca ha senha padrao no codigo. Idempotente: se o usuario ja existe, nao e alterado.
    /// </summary>
    public static class TestUserSeed
    {
        public static async Task RunAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            var config = sp.GetRequiredService<IConfiguration>();
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("TestUserSeed");

            var usuarioRaw = config["Seed:TestEmail"];
            var senha = config["Seed:TestSenha"];
            if (string.IsNullOrWhiteSpace(usuarioRaw) || string.IsNullOrEmpty(senha)) return;

            if (!EmailRules.IsValid(usuarioRaw))
            {
                log.LogError("Seed__TestEmail invalido (use um e-mail): usuario de teste nao criado.");
                return;
            }
            var usuario = EmailRules.Normalize(usuarioRaw);

            var perfil = config["Seed:TestPerfil"];
            if (string.IsNullOrWhiteSpace(perfil)) perfil = Roles.Viewer;
            if (!Roles.IsValid(perfil))
            {
                log.LogError("Seed__TestPerfil invalido (use '{Viewer}' ou '{Admin}'): usuario de teste nao criado.", Roles.Viewer, Roles.Admin);
                return;
            }

            var policyError = PasswordPolicy.Validate(senha, usuario);
            if (policyError is not null)
            {
                log.LogError("Seed__TestSenha rejeitada pela politica de senha ({Code}): usuario de teste nao criado.", policyError);
                return;
            }

            var db = sp.GetRequiredService<AppDbContext>();
            if (await db.Users.AnyAsync(u => u.Email == usuario)) return;

            var hasher = sp.GetRequiredService<Pbkdf2PasswordHasher>();
            db.Users.Add(new AppUser
            {
                Email = usuario,
                Role = perfil,
                PasswordHash = await hasher.HashAsync(senha),
                CreatedBy = "seed",
                UpdatedBy = "seed"
            });
            await db.SaveChangesAsync();

            await sp.GetRequiredService<AuditService>().LogAsync("usuario_teste_seed", "ok", "seed", usuario);
            log.LogInformation("Usuario de teste '{Usuario}' ({Perfil}) criado.", usuario, perfil);
        }
    }
}
