using backend.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace backend.Tests;

/// <summary>
/// Container PostgreSQL unico por execucao de testes (Testcontainers, exige Docker em execucao).
/// Cada <see cref="ApiFactory"/> usa um banco proprio dentro dele, para isolar os testes.
/// </summary>
public static class SharedPostgres
{
    private static readonly Lazy<Task<string>> Server = new(async () =>
    {
        var container = new PostgreSqlBuilder().WithImage("postgres:17").Build();
        await container.StartAsync();
        return container.GetConnectionString();
    });

    public static async Task<string> NewDatabaseAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(await Server.Value)
        {
            Database = $"test_{Guid.NewGuid():N}"
        };
        return builder.ConnectionString;
    }
}

/// <summary>
/// Fabrica de testes de integracao contra PostgreSQL real. O provedor e escolhido em
/// <see cref="ConfigureDatabase"/>; para outro banco basta herdar e sobrescrever.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Admin-Teste-123";
    public static readonly string CodeHmacKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 7)).ToArray());
    protected virtual bool SecureCookies => false;
    protected virtual bool CreateAdmin => true;
    protected virtual string HmacKeySetting => CodeHmacKey;

    private readonly string _connectionString = SharedPostgres.NewDatabaseAsync().GetAwaiter().GetResult();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Auth:SecureCookies", SecureCookies ? "true" : "false");
        builder.UseSetting("Auth:CodeHmacKey", HmacKeySetting);
        builder.UseSetting("Auth:AdminUsuario", CreateAdmin ? AdminUser : "");
        builder.UseSetting("Auth:AdminSenha", AdminPassword);
        builder.UseSetting("Auth:Pbkdf2Iterations", "1000");
        // limites globais altos para nao interferir (os testes de rate limit definem os seus)
        builder.UseSetting("RateLimit:WritePerMinute", "100000");
        builder.UseSetting("RateLimit:ReadPerMinute", "100000");
        builder.UseSetting("RateLimit:LoginFailuresPerMinute", "100000");
        builder.ConfigureServices(services =>
        {
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(DbContextOptions))
                .ToList();
            foreach (var d in descriptors) services.Remove(d);

            ConfigureDatabase(services, _connectionString);
        });
    }

    /// <summary>Registra o AppDbContext usado nos testes (padrao: Npgsql no banco isolado).</summary>
    protected virtual void ConfigureDatabase(IServiceCollection services, string connectionString) =>
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
}

/// <summary>Variante com cookies Secure (como em producao) para checar nome/flags do cookie.</summary>
public class SecureApiFactory : ApiFactory
{
    protected override bool SecureCookies => true;
}

public class NoAdminFactory : ApiFactory
{
    protected override bool CreateAdmin => false;
}

public class NoHmacKeyFactory : ApiFactory
{
    protected override string HmacKeySetting => "";
}
