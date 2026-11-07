using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace backend.Data
{
    /// <summary>Usada apenas por `dotnet ef` (migrations). Nao afeta a execucao da API.</summary>
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(DatabaseConnection.Resolve(config))
                .Options;
            return new AppDbContext(options);
        }
    }
}
