using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace backend.Tests;

/// <summary>Cliente de teste com jar de cookies manual e header X-XSRF-TOKEN automatico nas escritas (como o Angular).</summary>
public class SessionHandler : DelegatingHandler
{
    public Dictionary<string, string> Cookies { get; } = new();
    public bool SendXsrf { get; set; } = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Cookies.Count > 0)
            request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}")));
        var unsafeMethod = request.Method != HttpMethod.Get && request.Method != HttpMethod.Head;
        if (SendXsrf && unsafeMethod && Cookies.TryGetValue("XSRF-TOKEN", out var token) && !request.Headers.Contains("X-XSRF-TOKEN"))
            request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", token);

        var resp = await base.SendAsync(request, ct);
        if (resp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var sc in setCookies)
            {
                var first = sc.Split(';')[0];
                var eq = first.IndexOf('=');
                var name = first[..eq];
                var value = first[(eq + 1)..];
                var expired = sc.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || value.Length == 0;
                if (expired) Cookies.Remove(name); else Cookies[name] = value;
            }
        }
        return resp;
    }
}

public record ProblemBody(int Status, string? Code, string? Title, string? Detail, JsonElement Raw);

public static class AuthTestSupport
{
    public static (HttpClient Client, SessionHandler Session) NewSession(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var session = new SessionHandler();
        return (factory.CreateDefaultClient(session), session);
    }

    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string usuario, string senha) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email = usuario, senha });

    /// <summary>Cliente ja autenticado como o Admin inicial (bootstrap).</summary>
    public static HttpClient CreateAdminClient(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var (client, _) = factory.NewSession();
        var resp = client.LoginAsync(ApiFactory.AdminUser, ApiFactory.AdminPassword).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return client;
    }

    public static async Task<ProblemBody> ReadProblemAsync(this HttpResponseMessage resp)
    {
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return new ProblemBody((int)resp.StatusCode,
            json.TryGetProperty("code", out var c) ? c.GetString() : null,
            json.TryGetProperty("title", out var t) ? t.GetString() : null,
            json.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
            json);
    }

    /// <summary>Cria um usuario pelo Admin e devolve o codigo de ativacao.</summary>
    public static async Task<(int Id, string Code)> CreateUserAsync(this HttpClient admin, ApiFactory factory, string usuario, string perfil)
    {
        var resp = await admin.PostAsJsonAsync("/api/users", new { email = usuario, perfil });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // com o envio por e-mail o codigo nao vem na resposta: le-se do e-mail (fake) enviado a pessoa
        return (json.GetProperty("id").GetInt32(), CodeSentTo(factory, usuario));
    }

    /// <summary>Codigo de ativacao do ultimo e-mail enviado a esse endereco.</summary>
    public static string CodeSentTo(ApiFactory factory, string email)
    {
        var msg = factory.Email.Messages.Last(m => m.To == email);
        return System.Text.RegularExpressions.Regex.Match(msg.Text, "[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}").Value;
    }

    public static async Task<HttpResponseMessage> ActivateAsync(this HttpClient client, string usuario, string codigo, string novaSenha) =>
        await client.PostAsJsonAsync("/api/auth/ativar", new { email = usuario, codigo, novaSenha });

    /// <summary>Cria + ativa um usuario e devolve um cliente logado como ele.</summary>
    public static async Task<HttpClient> CreateUserClientAsync(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, HttpClient admin, string usuario,
        string perfil, string senha = "Senha-Forte-123")
    {
        var (_, code) = await admin.CreateUserAsync((ApiFactory)factory, usuario, perfil);
        var (client, _) = factory.NewSession();
        Assert.Equal(HttpStatusCode.NoContent, (await client.ActivateAsync(usuario, code, senha)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(usuario, senha)).StatusCode);
        return client;
    }
}
