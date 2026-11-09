using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Security
{
    /// <summary>
    /// Cria um usuario de teste para desenvolvimento a partir de Seed__TestUsuario / Seed__TestSenha
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

            var usuarioRaw = config["Seed:TestUsuario"];
            var senha = config["Seed:TestSenha"];
            if (string.IsNullOrWhiteSpace(usuarioRaw) || string.IsNullOrEmpty(senha)) return;

            if (!UsuarioRules.IsValid(usuarioRaw))
            {
                log.LogError("Seed__TestUsuario invalido (use 3-32 caracteres [a-z0-9._-]): usuario de teste nao criado.");
                return;
            }
            var usuario = UsuarioRules.Normalize(usuarioRaw);

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
            if (await db.Users.AnyAsync(u => u.Usuario == usuario)) return;

            var hasher = sp.GetRequiredService<Pbkdf2PasswordHasher>();
            db.Users.Add(new AppUser
            {
                Usuario = usuario,
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
