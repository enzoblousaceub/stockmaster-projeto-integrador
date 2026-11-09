using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using backend.Models;
using backend.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace backend.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthTests(ApiFactory factory) => _factory = factory;

    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> action, ApiFactory? factory = null)
    {
        using var scope = (factory ?? _factory).Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // ---------- deny-by-default ----------

    [Fact]
    public async Task Anonymous_ApiRequests_Return401ProblemDetails()
    {
        var client = _factory.CreateClient();
        foreach (var path in new[] { "/api/products", "/api/products/stats", "/api/products/1", "/api/users", "/api/audit", "/api/auth/me", "/api/rota-inexistente" })
        {
            var resp = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
            Assert.Equal("unauthenticated", (await resp.ReadProblemAsync()).Code);
        }
        var write = await client.PostAsJsonAsync("/api/products", new { name = "x", category = "c", quantity = 1, price = 1, minStock = 0 });
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_WithSession_Returns404NotSpaHtml()
    {
        var resp = await _factory.CreateAdminClient().GetAsync("/api/rota-inexistente");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PublicEndpoints_AreHealthzAndSpaOnly()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/qualquer/rota/do/spa")).StatusCode);
    }

    // ---------- login / sessao ----------

    [Fact]
    public async Task Login_Ok_ReturnsUsuarioPerfil_AndMe()
    {
        var (client, _) = _factory.NewSession();
        var resp = await client.LoginAsync("ADMIN", ApiFactory.AdminPassword);   // usuario case-insensitive
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin", json.GetProperty("usuario").GetString());
        Assert.Equal("Admin", json.GetProperty("perfil").GetString());

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("admin", me.GetProperty("usuario").GetString());
        Assert.Equal("Admin", me.GetProperty("perfil").GetString());
    }

    [Fact]
    public async Task Login_InvalidCredentials_AreIndistinguishable()
    {
        var (client, _) = _factory.NewSession();
        var wrongPassword = await client.LoginAsync("admin", "senha-errada-123");
        var unknownUser = await client.LoginAsync(Uniq("naoexiste"), "senha-errada-123");
        var pending = await client.LoginAsync(await PendingUser(), "qualquer-senha-123");

        foreach (var resp in new[] { wrongPassword, unknownUser, pending })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            var p = await resp.ReadProblemAsync();
            Assert.Equal("invalid_credentials", p.Code);
            Assert.Equal("Usuario ou senha invalidos", p.Title);
        }
    }

    private async Task<string> PendingUser()
    {
        var u = Uniq("pend");
        await _factory.CreateAdminClient().CreateUserAsync(u, "Visualizador");
        return u;
    }

    [Fact]
    public async Task Login_InvalidFormat_Returns400ValidationError()
    {
        var (client, _) = _factory.NewSession();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { usuario = "", senha = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var p = await resp.ReadProblemAsync();
        Assert.Equal("validation_error", p.Code);
        Assert.True(p.Raw.GetProperty("errors").TryGetProperty("usuario", out _));
    }

    [Fact]
    public async Task Logout_EndsSession_AndAllOtherSessionsOfThePerson()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("lo");
        var first = await _factory.CreateUserClientAsync(admin, user, "Visualizador");
        var (second, _) = _factory.NewSession();
        Assert.Equal(HttpStatusCode.OK, (await second.LoginAsync(user, "Senha-Forte-123")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);   // todas as sessoes caem
    }

    [Fact]
    public async Task StolenCookie_StopsWorking_AfterLogout()
    {
        var (client, session) = _factory.NewSession();
        await client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword);
        var stolen = new Dictionary<string, string>(session.Cookies);
        await client.PostAsync("/api/auth/logout", null);

        var (attacker, attackerSession) = _factory.NewSession();
        foreach (var (k, v) in stolen) attackerSession.Cookies[k] = v;
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Cookies_HaveSecureFlags()
    {
        using var factory = new SecureApiFactory();
        var client = factory.CreateDefaultClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");   // como atras do proxy do Render
        var resp = await client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword);
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"login: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
        var cookies = resp.Headers.GetValues("Set-Cookie").ToList();

        var auth = Assert.Single(cookies, c => c.StartsWith("__Host-sm.auth="));
        Assert.Contains("httponly", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", auth, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", auth, StringComparison.OrdinalIgnoreCase);

        var xsrf = Assert.Single(cookies, c => c.StartsWith("XSRF-TOKEN="));
        Assert.DoesNotContain("httponly", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", xsrf, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- bloqueio / rate limit ----------

    [Fact]
    public async Task Login_LocksAfterFiveFailures_Returns429WithRetryAfter_EvenForCorrectPassword()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("lk");
        await _factory.CreateUserClientAsync(admin, user, "Visualizador");
        var (client, _) = _factory.NewSession();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(user, "errada-errada-1")).StatusCode);

        var locked = await client.LoginAsync(user, "Senha-Forte-123");   // senha CERTA, mas bloqueado
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.TryGetValues("Retry-After", out var ra) && int.Parse(ra.Single()) > 0);
        var p = await locked.ReadProblemAsync();
        Assert.Equal("account_locked", p.Code);
        Assert.True(p.Raw.GetProperty("retryAfterSeconds").GetInt32() > 0);
    }

    [Fact]
    public async Task Login_LockIsIdenticalForUnknownUser_NoEnumeration()
    {
        var (client, _) = _factory.NewSession();
        var ghost = Uniq("fantasma");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(ghost, "errada-errada-1")).StatusCode);

        var locked = await client.LoginAsync(ghost, "errada-errada-1");
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("account_locked", (await locked.ReadProblemAsync()).Code);
    }

    [Fact]
    public async Task LoginLock_DoesNotAffectActivation_AndAdminCanUnlock()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("sep");
        var (id, code) = await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();

        for (var i = 0; i < 5; i++) await client.LoginAsync(user, "errada-errada-1");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.LoginAsync(user, "errada-errada-1")).StatusCode);

        // ativacao usa contador SEPARADO: continua liberada
        Assert.Equal(HttpStatusCode.NoContent, (await client.ActivateAsync(user, code, "Senha-Forte-123")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        var row = list.EnumerateArray().Single(u => u.GetProperty("usuario").GetString() == user);
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("bloqueadoAte").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{id}/desbloquear", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);
    }

    [Fact]
    public async Task Activation_LocksAfterFiveWrongCodes_AndIsIdenticalForUnknownUser()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("alk");
        var (_, code) = await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();

        foreach (var target in new[] { user, Uniq("fantasma") })
        {
            for (var i = 0; i < 5; i++)
            {
                var r = await client.ActivateAsync(target, "AAAA-BBBB-CCCC", "Senha-Forte-123");
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Equal("activation_code_invalid", (await r.ReadProblemAsync()).Code);
            }
            var locked = await client.ActivateAsync(target, "AAAA-BBBB-CCCC", "Senha-Forte-123");
            Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
            Assert.Equal("account_locked", (await locked.ReadProblemAsync()).Code);
        }

        // com o bloqueio ativo nem o codigo certo passa
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.ActivateAsync(user, code, "Senha-Forte-123")).StatusCode);
    }

    [Fact]
    public async Task IpFailureLimit_CountsOnlyFailures()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:LoginFailuresPerMinute", "5"));
        // sucessos nao consomem o limite
        for (var i = 0; i < 8; i++)
        {
            var (s, _) = factory.NewSession();
            Assert.Equal(HttpStatusCode.OK, (await s.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword)).StatusCode);
        }

        var (bad, _) = factory.NewSession();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await bad.LoginAsync(Uniq("ip"), "errada-errada-1")).StatusCode);
        var limited = await bad.LoginAsync(Uniq("ip"), "errada-errada-1");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", (await limited.ReadProblemAsync()).Code);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    // ---------- perfis ----------

    [Fact]
    public async Task Viewer_CanRead_ButCannotWriteOrManageUsers()
    {
        var admin = _factory.CreateAdminClient();
        var viewer = await _factory.CreateUserClientAsync(admin, Uniq("vw"), "Visualizador");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/products/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/products/categories")).StatusCode);

        var body = new { name = "x", category = "c", quantity = 1, price = 1, minStock = 0 };
        foreach (var resp in new[]
        {
            await viewer.PostAsJsonAsync("/api/products", body),
            await viewer.PutAsJsonAsync("/api/products/1", body),
            await viewer.PatchAsJsonAsync("/api/products/1/quantity", new { delta = 1 }),
            await viewer.DeleteAsync("/api/products/1"),
            await viewer.GetAsync("/api/users"),
            await viewer.PostAsJsonAsync("/api/users", new { usuario = "novo.user", perfil = "Admin" }),
            await viewer.GetAsync("/api/audit"),
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
            Assert.Equal("forbidden", (await resp.ReadProblemAsync()).Code);
        }
    }

    [Fact]
    public async Task Admin_CanWriteProducts()
    {
        var admin = _factory.CreateAdminClient();
        var resp = await admin.PostAsJsonAsync("/api/products", new { name = "Admin item", category = "c", quantity = 1, price = 1, minStock = 0 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    // ---------- CSRF ----------

    [Fact]
    public async Task Csrf_WriteWithoutToken_Returns403CsrfInvalid()
    {
        var (client, session) = _factory.NewSession();
        await client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword);
        session.SendXsrf = false;

        var resp = await client.PostAsJsonAsync("/api/products", new { name = "x", category = "c", quantity = 1, price = 1, minStock = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("csrf_invalid", (await resp.ReadProblemAsync()).Code);

        // token errado tambem
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/products/1");
        req.Headers.Add("X-XSRF-TOKEN", "token-invalido");
        Assert.Equal("csrf_invalid", (await (await client.SendAsync(req)).ReadProblemAsync()).Code);

        // GET nao exige token
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Csrf_TokenFromAnotherUser_IsRejected()
    {
        var admin = _factory.CreateAdminClient();
        var (a, sessionA) = _factory.NewSession();
        await a.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword);
        var (b, sessionB) = _factory.NewSession();
        var userB = Uniq("csrfb");
        var (_, code) = await admin.CreateUserAsync(userB, "Admin");
        await b.ActivateAsync(userB, code, "Senha-Forte-123");
        await b.LoginAsync(userB, "Senha-Forte-123");

        // usa o token do usuario B na sessao do usuario A
        sessionA.SendXsrf = false;
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(new { name = "x", category = "c", quantity = 1, price = 1, minStock = 0 })
        };
        req.Headers.Add("X-XSRF-TOKEN", sessionB.Cookies["XSRF-TOKEN"]);
        var resp = await a.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("csrf_invalid", (await resp.ReadProblemAsync()).Code);
    }

    [Theory]
    [InlineData("Origin", "https://evil.example")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    public async Task Csrf_CrossSiteSignals_AreRejected_EvenOnLogin(string header, string value)
    {
        var (client, _) = _factory.NewSession();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { usuario = ApiFactory.AdminUser, senha = ApiFactory.AdminPassword })
        };
        req.Headers.Add(header, value);
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("csrf_invalid", (await resp.ReadProblemAsync()).Code);
    }

    [Fact]
    public async Task Csrf_SameOriginSignals_AreAccepted()
    {
        var (client, _) = _factory.NewSession();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { usuario = ApiFactory.AdminUser, senha = ApiFactory.AdminPassword })
        };
        req.Headers.Add("Origin", "http://localhost");
        req.Headers.Add("Sec-Fetch-Site", "same-origin");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
    }

    // ---------- usuarios / ativacao ----------

    [Fact]
    public async Task CreateUser_ReturnsOneTimeCode_AndUserStartsPending()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("cu");
        var resp = await admin.PostAsJsonAsync("/api/users", new { usuario = user.ToUpper(), perfil = "Visualizador" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user, json.GetProperty("usuario").GetString());
        Assert.Equal("Pendente", json.GetProperty("status").GetString());
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", json.GetProperty("codigoAtivacao").GetString()!);
        var expires = json.GetProperty("codigoExpiraEm").GetDateTime();
        Assert.InRange((expires - DateTime.UtcNow).TotalHours, 47.9, 48.1);
        Assert.Equal("admin", json.GetProperty("criadoPor").GetString());

        // o codigo nunca mais aparece, e o banco guarda so o HMAC
        var listText = await admin.GetStringAsync("/api/users");
        Assert.DoesNotContain(json.GetProperty("codigoAtivacao").GetString()!, listText);
        Assert.DoesNotContain("codigoAtivacao", listText);
        var stored = await Db(db => db.Users.Where(u => u.Usuario == user).Select(u => u.ActivationCodeHash).SingleAsync());
        Assert.NotNull(stored);
        Assert.DoesNotContain(json.GetProperty("codigoAtivacao").GetString()!.Replace("-", ""), stored);
    }

    [Fact]
    public async Task CreateUser_Validations()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("dup");
        await admin.CreateUserAsync(user, "Visualizador");

        var dup = await admin.PostAsJsonAsync("/api/users", new { usuario = user, perfil = "Visualizador" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("usuario_exists", (await dup.ReadProblemAsync()).Code);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { usuario = "a b", perfil = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { usuario = "ok.user", perfil = "admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { usuario = "ok.user", perfil = "Viewer" })).StatusCode);
    }

    [Fact]
    public async Task Activation_FullFlow_ThenLogin()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("flow");
        var (_, code) = await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);   // pendente
        // codigo aceito sem hifens e em minusculas
        Assert.Equal(HttpStatusCode.NoContent, (await client.ActivateAsync(user, code.Replace("-", "").ToLower(), "Senha-Forte-123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        var row = list.EnumerateArray().Single(u => u.GetProperty("usuario").GetString() == user);
        Assert.Equal("Ativo", row.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("ultimoLoginEm").ValueKind);
    }

    [Fact]
    public async Task Activation_InvalidPassword_DoesNotConsumeCodeNorCountAsFailure()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("pw");
        var (_, code) = await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();

        var tooShort = await client.ActivateAsync(user, code, "curta");
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        var p = await tooShort.ReadProblemAsync();
        Assert.Equal("password_too_short", p.Code);
        Assert.True(p.Raw.GetProperty("errors").TryGetProperty("novaSenha", out _));

        // varias senhas fracas (mais que o limite de falhas): nada disso e tentativa errada de codigo
        for (var i = 0; i < 7; i++)
        {
            var weak = await client.ActivateAsync(user, code, i % 2 == 0 ? "12345678" : user);
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            Assert.Equal("password_weak", (await weak.ReadProblemAsync()).Code);
        }

        // o mesmo codigo ainda vale
        Assert.Equal(HttpStatusCode.NoContent, (await client.ActivateAsync(user, code, "Senha-Forte-123")).StatusCode);
    }

    [Fact]
    public async Task Activation_CodeErrors_InvalidUsedExpired()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("ce");
        var (_, code) = await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();

        var wrong = await client.ActivateAsync(user, "AAAA-BBBB-CCCC", "Senha-Forte-123");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("activation_code_invalid", (await wrong.ReadProblemAsync()).Code);

        var unknown = await client.ActivateAsync(Uniq("ghost"), code, "Senha-Forte-123");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("activation_code_invalid", (await unknown.ReadProblemAsync()).Code);

        var badFormat = await client.ActivateAsync(user, "curto", "Senha-Forte-123");
        Assert.Equal("validation_error", (await badFormat.ReadProblemAsync()).Code);

        // expirado (so e revelado porque o codigo confere)
        var expiredUser = Uniq("ex");
        var (_, expiredCode) = await admin.CreateUserAsync(expiredUser, "Visualizador");
        await Db(async db =>
        {
            await db.Users.Where(u => u.Usuario == expiredUser)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ActivationExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            return 0;
        });
        var expired = await client.ActivateAsync(expiredUser, expiredCode, "Senha-Forte-123");
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal("activation_code_expired", (await expired.ReadProblemAsync()).Code);

        // usado
        Assert.Equal(HttpStatusCode.NoContent, (await client.ActivateAsync(user, code, "Senha-Forte-123")).StatusCode);
        var used = await client.ActivateAsync(user, code, "Outra-Senha-456");
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
        Assert.Equal("activation_code_used", (await used.ReadProblemAsync()).Code);
        // e a senha nao foi trocada
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);
    }

    [Fact]
    public async Task Activation_IsAtomic_OnlyOneConcurrentRequestWins()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("race");
        var (_, code) = await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();

        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(i => client.ActivateAsync(user, code, $"Senha-Forte-12{i}")));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.NoContent), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Reset_InvalidatesPasswordAndSessions_AndIssuesNewCode()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("rs");
        var session = await _factory.CreateUserClientAsync(admin, user, "Visualizador");
        var id = (await Db(db => db.Users.Where(u => u.Usuario == user).Select(u => u.Id).SingleAsync()));

        var resp = await admin.PostAsync($"/api/users/{id}/reset", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var newCode = json.GetProperty("codigoAtivacao").GetString()!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);   // sessao caiu
        var (client, _) = _factory.NewSession();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);   // senha antiga morreu

        Assert.Equal(HttpStatusCode.NoContent, (await client.ActivateAsync(user, newCode, "Nova-Senha-456")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, "Nova-Senha-456")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/users/999999/reset", null)).StatusCode);
    }

    [Fact]
    public async Task Patch_DeactivateReactivate_IsIdempotent_AndKillsSession()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("pt");
        var session = await _factory.CreateUserClientAsync(admin, user, "Visualizador");
        var id = await Db(db => db.Users.Where(u => u.Usuario == user).Select(u => u.Id).SingleAsync());

        var off = await admin.PatchAsJsonAsync($"/api/users/{id}", new { ativo = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal("Inativo", (await off.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        var (client, _) = _factory.NewSession();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);

        // repetir = 200 sem mudanca
        var stampBefore = await Db(db => db.Users.Where(u => u.Id == id).Select(u => u.SecurityStamp).SingleAsync());
        var again = await admin.PatchAsJsonAsync($"/api/users/{id}", new { ativo = false });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(stampBefore, await Db(db => db.Users.Where(u => u.Id == id).Select(u => u.SecurityStamp).SingleAsync()));

        // reset de inativo -> 409
        var reset = await admin.PostAsync($"/api/users/{id}/reset", null);
        Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
        Assert.Equal("user_inactive", (await reset.ReadProblemAsync()).Code);

        var on = await admin.PatchAsJsonAsync($"/api/users/{id}", new { ativo = true });
        Assert.Equal("Ativo", (await on.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);
        var onAgain = await admin.PatchAsJsonAsync($"/api/users/{id}", new { ativo = true });
        Assert.Equal(HttpStatusCode.OK, onAgain.StatusCode);
    }

    [Fact]
    public async Task Patch_ReactivatingPendingUserWithoutPassword_IssuesNewCode()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("pr");
        var (id, oldCode) = await admin.CreateUserAsync(user, "Visualizador");
        await admin.PatchAsJsonAsync($"/api/users/{id}", new { ativo = false });
        var on = await admin.PatchAsJsonAsync($"/api/users/{id}", new { ativo = true });
        var json = await on.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pendente", json.GetProperty("status").GetString());
        var newCode = json.GetProperty("codigoAtivacao").GetString()!;
        Assert.NotEqual(oldCode, newCode);

        var (client, _) = _factory.NewSession();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.ActivateAsync(user, oldCode, "Senha-Forte-123")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.ActivateAsync(user, newCode, "Senha-Forte-123")).StatusCode);
    }

    [Fact]
    public async Task Patch_ChangingProfile_RevokesSessions_AndRejectsBadInput()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("pf");
        var session = await _factory.CreateUserClientAsync(admin, user, "Visualizador");
        var id = await Db(db => db.Users.Where(u => u.Usuario == user).Select(u => u.Id).SingleAsync());

        // campo desconhecido, perfil fora do enum (case-sensitive), corpo vazio
        var unknown = await admin.PatchAsJsonAsync($"/api/users/{id}", new { perfil = "Admin", senha = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("validation_error", (await unknown.ReadProblemAsync()).Code);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/users/{id}", new { perfil = "admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/users/{id}", new { perfil = "Superuser" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/users/{id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/auth/me")).StatusCode);   // nada mudou

        var ok = await admin.PatchAsJsonAsync($"/api/users/{id}", new { perfil = "Admin" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync("/api/users/999999", new { ativo = false })).StatusCode);
    }

    [Fact]
    public async Task LastAdmin_IsProtected_OnPatchAndReset()
    {
        // fabrica propria: banco proprio com exatamente um Admin
        using var factory = new ApiFactory();
        var admin = factory.CreateAdminClient();
        var adminId = await Db(db => db.Users.Where(u => u.Usuario == ApiFactory.AdminUser).Select(u => u.Id).SingleAsync(), factory);

        foreach (var resp in new[]
        {
            await admin.PatchAsJsonAsync($"/api/users/{adminId}", new { ativo = false }),
            await admin.PatchAsJsonAsync($"/api/users/{adminId}", new { perfil = "Visualizador" }),
            await admin.PostAsync($"/api/users/{adminId}/reset", null),
        })
        {
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            Assert.Equal("last_admin", (await resp.ReadProblemAsync()).Code);
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);

        // com um segundo Admin ativo, o primeiro pode ser rebaixado
        await factory.CreateUserClientAsync(admin, "segundo.admin", "Admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/users/{adminId}", new { perfil = "Visualizador" })).StatusCode);
    }

    [Fact]
    public async Task ChangeOwnPassword_Flow()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("cp");
        var current = await _factory.CreateUserClientAsync(admin, user, "Visualizador");
        var (other, _) = _factory.NewSession();
        await other.LoginAsync(user, "Senha-Forte-123");

        // senha atual errada: 400 (nao 401, para o front nao deslogar)
        var wrong = await current.PostAsJsonAsync("/api/auth/senha", new { senhaAtual = "errada-errada-1", novaSenha = "Nova-Senha-456" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("current_password_invalid", (await wrong.ReadProblemAsync()).Code);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal("password_too_short", (await (await current.PostAsJsonAsync("/api/auth/senha", new { senhaAtual = "Senha-Forte-123", novaSenha = "curta" })).ReadProblemAsync()).Code);
        Assert.Equal("password_weak", (await (await current.PostAsJsonAsync("/api/auth/senha", new { senhaAtual = "Senha-Forte-123", novaSenha = "12345678" })).ReadProblemAsync()).Code);

        var ok = await current.PostAsJsonAsync("/api/auth/senha", new { senhaAtual = "Senha-Forte-123", novaSenha = "Nova-Senha-456" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/auth/me")).StatusCode);          // esta sessao continua
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);   // as outras caem
        var (fresh, _) = _factory.NewSession();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.LoginAsync(user, "Senha-Forte-123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.LoginAsync(user, "Nova-Senha-456")).StatusCode);
        // sessao renovada continua conseguindo escrever (token CSRF reemitido)
        Assert.Equal(HttpStatusCode.NoContent, (await current.PostAsync("/api/auth/logout", null)).StatusCode);
    }

    // ---------- auditoria ----------

    [Fact]
    public async Task Audit_RecordsEvents_WithoutSecrets_AdminOnly()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("au");
        var (id, code) = await admin.CreateUserAsync(user, "Visualizador");
        var (client, _) = _factory.NewSession();
        await client.LoginAsync(user, "senha-que-nao-vale");
        await client.ActivateAsync(user, code, "Senha-Super-Secreta-789");
        await client.LoginAsync(user, "Senha-Super-Secreta-789");
        await admin.PostAsync($"/api/users/{id}/reset", null);

        var text = await admin.GetStringAsync("/api/audit?limite=500");
        foreach (var evento in new[] { "usuario_criado", "login_falha", "ativacao_ok", "login_ok", "usuario_resetado" })
            Assert.Contains(evento, text);
        Assert.DoesNotContain("Senha-Super-Secreta-789", text);
        Assert.DoesNotContain("senha-que-nao-vale", text);
        Assert.DoesNotContain(code, text);
        Assert.DoesNotContain(code.Replace("-", ""), text);

        var rows = JsonDocument.Parse(text).RootElement.EnumerateArray().ToList();
        var created = rows.First(r => r.GetProperty("evento").GetString() == "usuario_criado" && r.GetProperty("alvoUsuario").GetString() == user);
        Assert.Equal("admin", created.GetProperty("atorUsuario").GetString());
        Assert.Equal("ok", created.GetProperty("resultado").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit?limite=9999")).StatusCode);   // limite e ajustado, nao erro
    }

    [Fact]
    public async Task Retention_PurgesAuditOlderThan90Days()
    {
        var oldMarker = Uniq("old");
        var freshMarker = Uniq("new");
        await Db(async db =>
        {
            db.AuditLogs.Add(new AuditLog { Evento = "login_ok", Resultado = "ok", AlvoUsuario = oldMarker, Ip = "10.0.0.1", OccurredAt = DateTime.UtcNow.AddDays(-91) });
            db.AuditLogs.Add(new AuditLog { Evento = "login_ok", Resultado = "ok", AlvoUsuario = freshMarker, Ip = "10.0.0.2", OccurredAt = DateTime.UtcNow.AddDays(-89) });
            await db.SaveChangesAsync();
            return 0;
        });

        await RetentionService.PurgeAsync(_factory.Services, CancellationToken.None);

        Assert.False(await Db(db => db.AuditLogs.AnyAsync(a => a.AlvoUsuario == oldMarker)));
        Assert.True(await Db(db => db.AuditLogs.AnyAsync(a => a.AlvoUsuario == freshMarker)));
    }

    // ---------- hash ----------

    [Fact]
    public void Hasher_Uses210kInProduction_AndRoundTrips()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Pbkdf2Iterations"] = "1" }).Build();
        var prod = new Pbkdf2PasswordHasher(cfg, new HostingEnvironment { EnvironmentName = "Production" });
        var hash = prod.Hash("Senha-Forte-123");
        Assert.StartsWith("pbkdf2-sha512$210000$", hash);   // piso de 210k mesmo com config menor
        Assert.True(prod.Verify(hash, "Senha-Forte-123"));
        Assert.False(prod.Verify(hash, "senha-forte-123"));
        Assert.False(prod.Verify("lixo", "x"));
        Assert.NotEqual(hash, prod.Hash("Senha-Forte-123"));   // sal aleatorio
        Assert.False(prod.NeedsRehash(hash));
    }

    [Fact]
    public async Task Login_RehashesPasswordWhenIterationsAreBelowCurrent()
    {
        var admin = _factory.CreateAdminClient();
        var user = Uniq("rh");
        await _factory.CreateUserClientAsync(admin, user, "Visualizador");   // hash com 1000 iteracoes

        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:Pbkdf2Iterations", "2000"));
        var (client, _) = factory.NewSession();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);

        var hash = await Db(db => db.Users.Where(u => u.Usuario == user).Select(u => u.PasswordHash).SingleAsync());
        Assert.StartsWith("pbkdf2-sha512$2000$", hash);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, "Senha-Forte-123")).StatusCode);
    }

    [Fact]
    public void ActivationCodes_FormatNormalizeAndConstantTimeMatch()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:CodeHmacKey"] = ApiFactory.CodeHmacKey }).Build();
        var codes = new ActivationCodes(cfg);
        Assert.True(codes.IsConfigured);

        var code = ActivationCodes.Generate();
        var normalized = ActivationCodes.Normalize(code)!;
        Assert.Equal(12, normalized.Length);
        Assert.Equal(normalized, ActivationCodes.Normalize(code.ToLower().Replace("-", " ")));
        Assert.Null(ActivationCodes.Normalize("curto"));
        Assert.Null(ActivationCodes.Normalize("ILOU-ILOU-ILOU"));   // I, L, O, U nao existem no alfabeto

        var hash = codes.Hash("maria", normalized);
        Assert.True(codes.Matches(hash, "maria", normalized));
        Assert.False(codes.Matches(hash, "joao", normalized));       // amarrado ao usuario
        Assert.False(codes.Matches(null, "maria", normalized));

        Assert.False(new ActivationCodes(new ConfigurationBuilder().Build()).IsConfigured);
        var shortKey = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:CodeHmacKey"] = Convert.ToBase64String(new byte[8]) }).Build();
        Assert.False(new ActivationCodes(shortKey).IsConfigured);
    }

    [Theory]
    [InlineData("curta", "password_too_short")]
    [InlineData("12345678", "password_weak")]
    [InlineData("aaaaaaaaaaaa", "password_weak")]
    [InlineData("maria.silva", "password_weak")]
    [InlineData("Senha-Forte-123", null)]
    public void PasswordPolicy_Rules(string password, string? expected) =>
        Assert.Equal(expected, PasswordPolicy.Validate(password, "maria.silva"));
}

public class AdminBootstrapTests
{
    [Fact]
    public async Task WithoutEnvVars_NoAdminIsCreated_AndNoDefaultPassword()
    {
        using var factory = new NoAdminFactory();
        var (client, _) = factory.NewSession();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync("admin", ApiFactory.AdminPassword)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Bootstrap_RecoversAdmin_WhenNoActiveAdminRemains()
    {
        using var factory = new ApiFactory();
        _ = factory.CreateAdminClient();   // bootstrap inicial
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        using var restarted = factory.WithWebHostBuilder(_ => { });
        var (client, _) = restarted.NewSession();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword)).StatusCode);
        var audit = await client.GetStringAsync("/api/audit");
        Assert.Contains("admin_recuperado", audit);
    }

    [Fact]
    public async Task WeakBootstrapPassword_IsRejected()
    {
        using var factory = new NoAdminFactory().WithWebHostBuilder(b =>
        {
            b.UseSetting("Auth:AdminUsuario", "admin");
            b.UseSetting("Auth:AdminSenha", "12345678");
        });
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task MissingHmacKey_BlocksCodeIssuing_WithServiceMisconfigured()
    {
        using var factory = new NoHmacKeyFactory();
        var admin = factory.CreateAdminClient();
        var resp = await admin.PostAsJsonAsync("/api/users", new { usuario = "novo.user", perfil = "Visualizador" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Equal("service_misconfigured", (await resp.ReadProblemAsync()).Code);
    }
}

public class TestUserSeedTests
{
    private const string TestUser = "teste";
    private const string TestPassword = "Teste-Seed-123";

    private static WebApplicationFactory<Program> WithTestUser(ApiFactory factory, string? perfil = null, string seedEnabled = "true") =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Seed:Enabled", seedEnabled);
            b.UseSetting("Seed:TestUsuario", TestUser);
            b.UseSetting("Seed:TestSenha", TestPassword);
            if (perfil is not null) b.UseSetting("Seed:TestPerfil", perfil);
        });

    [Fact]
    public async Task WithVars_CreatesViewerByDefault_AndCanLogin()
    {
        using var baseFactory = new ApiFactory();
        using var factory = WithTestUser(baseFactory);
        var client = factory.CreateClient(new() { HandleCookies = true });
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(TestUser, TestPassword)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Usuario == TestUser);
        Assert.Equal(Roles.Viewer, user.Role);
    }

    [Fact]
    public async Task WithAdminProfile_CreatesAdmin()
    {
        using var baseFactory = new ApiFactory();
        using var factory = WithTestUser(baseFactory, Roles.Admin);
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Usuario == TestUser);
        Assert.Equal(Roles.Admin, user.Role);
    }

    [Fact]
    public async Task SeedDisabled_DoesNotCreateTestUser()
    {
        using var baseFactory = new ApiFactory();
        using var factory = WithTestUser(baseFactory, seedEnabled: "false");
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Usuario == TestUser));
    }

    [Fact]
    public async Task WithoutVars_NoTestUserIsCreated()
    {
        using var factory = new ApiFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }
}
