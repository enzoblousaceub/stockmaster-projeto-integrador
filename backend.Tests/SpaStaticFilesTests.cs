using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace backend.Tests;

/// <summary>
/// Em producao o backend serve o build do Angular (wwwroot/browser). Os arquivos estaticos precisam ser
/// publicos: com a politica deny-by-default, se forem servidos depois da autorizacao o JS/CSS volta 401
/// e a tela de login fica em branco.
/// </summary>
public class SpaStaticFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"spa_{Guid.NewGuid():N}");
    private readonly ApiFactory _baseFactory = new();

    public SpaStaticFilesTests()
    {
        var browser = Directory.CreateDirectory(Path.Combine(_root, "wwwroot", "browser")).FullName;
        File.WriteAllText(Path.Combine(browser, "index.html"), "<!doctype html><html><body><app-root></app-root></body></html>");
        File.WriteAllText(Path.Combine(browser, "main-TEST.js"), "console.log('ok');");
        File.WriteAllText(Path.Combine(browser, "styles-TEST.css"), "body{}");
        File.WriteAllText(Path.Combine(browser, "manifest.webmanifest"), "{}");
    }

    private HttpClient AnonymousClient() =>
        _baseFactory.WithWebHostBuilder(b => b.UseContentRoot(_root)).CreateClient();

    [Theory]
    [InlineData("/main-TEST.js", "text/javascript")]
    [InlineData("/styles-TEST.css", "text/css")]
    [InlineData("/manifest.webmanifest", "application/manifest+json")]
    public async Task StaticAssets_AreServedWithoutLogin(string path, string contentType)
    {
        var resp = await AnonymousClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(contentType, resp.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/produtos/1")]
    public async Task SpaRoutes_ReturnIndexHtml_WithoutLogin(string path)
    {
        var resp = await AnonymousClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("<app-root>", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_StillRequiresLogin()
    {
        var client = AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/nao-existe")).StatusCode);
    }

    public void Dispose()
    {
        _baseFactory.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }
}
