using System.Net;
using System.Security.Cryptography;
using System.Text;
using backend.Security;

namespace backend.Tests;

public class SecurityHeadersTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SecurityHeadersTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/products");
        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("strict-origin", resp.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", resp.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("no-store", resp.Headers.GetValues("Cache-Control").Single());

        var csp = resp.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
    }

    [Fact]
    public async Task Hsts_OnlyOverHttps()
    {
        var http = await _factory.CreateClient().GetAsync("/api/products");
        Assert.False(http.Headers.Contains("Strict-Transport-Security"));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var https = await client.GetAsync("/api/products");
        Assert.Contains("max-age=", https.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Fact]
    public async Task Cors_IsOffByDefault()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        req.Headers.Add("Origin", "https://evil.example");
        var resp = await _factory.CreateClient().SendAsync(req);
        Assert.False(resp.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_AllowsOnlyConfiguredOrigins()
    {
        using var factory = _factory.WithWebHostBuilder(b =>
            b.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200"));
        var client = factory.CreateClient();

        var ok = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        ok.Headers.Add("Origin", "http://localhost:4200");
        Assert.Equal("http://localhost:4200",
            (await client.SendAsync(ok)).Headers.GetValues("Access-Control-Allow-Origin").Single());

        var evil = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        evil.Headers.Add("Origin", "https://evil.example");
        Assert.False((await client.SendAsync(evil)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void InlineScriptHashes_HashesOnlyInlineScripts()
    {
        const string body = "var a=1;";
        var html = $"<html><head><script>{body}</script><script src=\"main.js\" type=\"module\"></script></head></html>";
        var expected = $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)))}'";

        Assert.Equal(new[] { expected }, SecurityHeaders.InlineScriptHashes(html));
        Assert.Contains(expected, SecurityHeaders.BuildCsp(new[] { expected }));
    }
}
