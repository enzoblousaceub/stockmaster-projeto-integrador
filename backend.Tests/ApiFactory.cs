using backend.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

/// <summary>
/// Fabrica de testes de integracao. O provedor do banco e escolhido em
/// <see cref="ConfigureDatabase"/>: para migrar para PostgreSQL (ex.: Testcontainers),
/// basta herdar desta classe e sobrescrever esse metodo; os testes nao mudam.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(DbContextOptions))
                .ToList();
            foreach (var d in descriptors) services.Remove(d);

            ConfigureDatabase(services);
        });
    }

    /// <summary>Registra o AppDbContext usado nos testes (padrao: SQLite em memoria).</summary>
    protected virtual void ConfigureDatabase(IServiceCollection services)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open(); // conexao aberta mantem o banco em memoria vivo
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection?.Dispose();
    }
}
