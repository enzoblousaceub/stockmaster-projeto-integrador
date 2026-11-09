using backend.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace backend.Tests;

public class DatabaseConnectionTests
{
    [Fact]
    public void FromUrl_ParsesRenderUrl_AndRequiresTls()
    {
        var cs = new NpgsqlConnectionStringBuilder(
            DatabaseConnection.FromUrl("postgresql://usr:p%40ss%3Aw@dpg-abc.oregon-postgres.render.com:5432/stock_db"));
        Assert.Equal("dpg-abc.oregon-postgres.render.com", cs.Host);
        Assert.Equal(5432, cs.Port);
        Assert.Equal("stock_db", cs.Database);
        Assert.Equal("usr", cs.Username);
        Assert.Equal("p@ss:w", cs.Password);
        Assert.Equal(SslMode.Require, cs.SslMode);
        Assert.True(cs.TrustServerCertificate); // cert interno do Render e autoassinado
    }

    [Fact]
    public void FromUrl_DefaultPort_AndLocalhostWithoutTls()
    {
        var cs = new NpgsqlConnectionStringBuilder(DatabaseConnection.FromUrl("postgres://u:p@localhost/db"));
        Assert.Equal(5432, cs.Port);
        Assert.Equal(SslMode.Disable, cs.SslMode);
    }

    [Fact]
    public void FromUrl_HonorsSslModeQuery()
    {
        var cs = new NpgsqlConnectionStringBuilder(DatabaseConnection.FromUrl("postgresql://u:p@host:5433/db?sslmode=disable"));
        Assert.Equal(5433, cs.Port);
        Assert.Equal(SslMode.Disable, cs.SslMode);
    }

    [Theory]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    [InlineData("verify-full", SslMode.VerifyFull)]
    public void FromUrl_KeepsStrictSslModes(string mode, SslMode expected)
    {
        var cs = new NpgsqlConnectionStringBuilder(DatabaseConnection.FromUrl($"postgresql://u:p@host/db?sslmode={mode}"));
        Assert.Equal(expected, cs.SslMode);
        Assert.False(cs.TrustServerCertificate);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("mysql://u:p@h/db")]
    public void FromUrl_RejectsInvalid_WithoutLeakingValue(string url)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseConnection.FromUrl(url));
        Assert.DoesNotContain(url, ex.Message);
    }

    [Fact]
    public void Resolve_PrefersDatabaseUrl_ThenConnectionString_ThenThrows()
    {
        var both = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://u:p@h:5432/fromurl",
            ["ConnectionStrings:Default"] = "Host=x;Database=fromcs"
        }).Build();
        Assert.Contains("fromurl", DatabaseConnection.Resolve(both));

        var onlyCs = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=x;Database=fromcs"
        }).Build();
        Assert.Contains("fromcs", DatabaseConnection.Resolve(onlyCs));

        Assert.Throws<InvalidOperationException>(() => DatabaseConnection.Resolve(new ConfigurationBuilder().Build()));
    }
}
