using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using backend.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace backend.Tests;

/// <summary>Correcoes da revisao do Escudo (rodada 1): concorrencia do hash, auditoria deduplicada, chaves cifradas etc.</summary>
public class AuthHardeningTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthHardeningTests(ApiFactory factory) => _factory = factory;

    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> action, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? f = null)
    {
        using var scope = (f ?? _factory).Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Hasher_Overloaded_ThrowsBusy_InsteadOfQueueingForever()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:HashConcurrency"] = "1",
            ["Auth:HashWaitMs"] = "1"
        }).Build();
        var hasher = new Pbkdf2PasswordHasher(cfg, new HostingEnvironment { EnvironmentName = "Production" });

        var tasks = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            try { await hasher.HashAsync("Senha-Forte-123"); return true; }
            catch (HasherBusyException) { return false; }
        })).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Contains(true, results);    // alguns passam
        Assert.Contains(false, results);   // o excedente e recusado rapidamente
    }

    [Fact]
    public void Hasher_CorruptedHash_ReturnsFalse_NeverThrows()
    {
        var hasher = new Pbkdf2PasswordHasher(new ConfigurationBuilder().Build(), new HostingEnvironment { EnvironmentName = "Production" });
        Assert.False(hasher.Verify("pbkdf2-sha512$210000$AAAA$", "x"));         // hash vazio (expected.Length == 0)
        Assert.False(hasher.Verify("pbkdf2-sha512$210000$%%%$%%%", "x"));       // base64 invalido
        Assert.False(hasher.Verify("pbkdf2-sha512$abc$AAAA$AAAA", "x"));
    }

    [Fact]
    public async Task BlockedAttempts_AreAuditedOncePerMinute_NotPerRequest()
    {
        var ghost = Uniq("aud");
        var (client, _) = _factory.NewSession();
        for (var i = 0; i < 5; i++) await client.LoginAsync(ghost, "errada-errada-1");
        for (var i = 0; i < 15; i++)
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.LoginAsync(ghost, "errada-errada-1")).StatusCode);

        var blocked = await Db(db => db.AuditLogs.CountAsync(a => a.Evento == "login_bloqueado" && a.AlvoUsuario == ghost));
        Assert.Equal(1, blocked);
    }

    [Fact]
    public async Task UserList_ShowsActivationLockInBloqueadoAte()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("ba");
        await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();
        for (var i = 0; i < 5; i++) await client.ActivateAsync(user, "AAAA-BBBB-CCCC", "Senha-Forte-123");

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        var row = list.EnumerateArray().Single(u => u.GetProperty("usuario").GetString() == user);
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("bloqueadoAte").ValueKind);
    }

    [Fact]
    public async Task SecureCookies_WithoutForwardedProto_FailsClearly_Not500()
    {
        using var factory = new SecureApiFactory();
        var client = factory.CreateDefaultClient();   // sem X-Forwarded-Proto: chegou por HTTP
        var resp = await client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("https_required", (await resp.ReadProblemAsync()).Code);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);   // healthz continua livre
    }

    [Fact]
    public async Task DataProtectionKeys_AreEncryptedAtRest_AndSurviveRestart()
    {
        var kek = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)(i * 3)).ToArray());
        using var baseFactory = new ApiFactory();
        using var first = baseFactory.WithWebHostBuilder(b => b.UseSetting("Auth:KeyEncryptionKey", kek));
        var (client, session) = first.NewSession();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword)).StatusCode);

        var xml = await Db(db => db.DataProtectionKeys.Select(k => k.Xml).ToListAsync(), first);
        Assert.NotEmpty(xml);
        Assert.All(xml, x =>
        {
            Assert.DoesNotContain("masterKey", x);    // material da chave nunca em texto
            Assert.True(x.Contains("<enc") && x.Contains("EnvKeyXmlDecryptor"), x);
        });

        // "reinicio": novo host, mesmo banco e mesma KEK -> a sessao antiga continua valida
        using var second = baseFactory.WithWebHostBuilder(b => b.UseSetting("Auth:KeyEncryptionKey", kek));
        var (client2, session2) = second.NewSession();
        foreach (var (k, v) in session.Cookies) session2.Cookies[k] = v;
        Assert.Equal(HttpStatusCode.OK, (await client2.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Admins_DemotingEachOtherConcurrently_NeverLeaveZeroAdmins()
    {
        using var factory = new ApiFactory();
        var first = factory.CreateAdminClient();
        var second = await factory.CreateUserClientAsync(first, "admin.dois", "Admin");
        var firstId = await Db(db => db.Users.Where(u => u.Usuario == "admin").Select(u => u.Id).SingleAsync(), factory);
        var secondId = await Db(db => db.Users.Where(u => u.Usuario == "admin.dois").Select(u => u.Id).SingleAsync(), factory);

        var results = await Task.WhenAll(
            first.PatchAsJsonAsync($"/api/users/{secondId}", new { perfil = "Visualizador" }),
            second.PatchAsJsonAsync($"/api/users/{firstId}", new { perfil = "Visualizador" }));

        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.Conflict);   // a segunda e barrada por last_admin
        var admins = await Db(db => db.Users.CountAsync(u => u.IsActive && u.PasswordHash != null && u.Role == "Admin"), factory);
        Assert.Equal(1, admins);
    }

    [Fact]
    public async Task StaticAndHealthRequests_DoNotValidateSessionInDatabase()
    {
        // um cookie de sessao revogada nao quebra o carregamento do SPA/healthz (so /api valida a sessao)
        var (client, session) = _factory.NewSession();
        await client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword);
        session.Cookies["sm.auth"] = session.Cookies["sm.auth"] + "x";   // cookie adulterado
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
    }
}
