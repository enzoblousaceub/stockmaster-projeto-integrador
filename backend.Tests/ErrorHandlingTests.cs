using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

public class ErrorHandlingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ErrorHandlingTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAdminClient();
    }

    [Fact]
    public async Task NotFound_ReturnsProblemDetails()
    {
        var resp = await _client.GetAsync("/api/products/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.Contains("999999", json.GetProperty("detail").GetString());
        Assert.True(json.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task ValidationError_ReturnsProblemDetailsWithErrors()
    {
        var resp = await _client.PostAsJsonAsync("/api/products",
            new { name = "", category = "C", quantity = -1, price = 1, minStock = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var errors = json.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Quantity", out _));
        Assert.True(json.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task MalformedJson_Returns400ProblemDetails()
    {
        var resp = await _client.PostAsync("/api/products",
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnhandledException_Returns500WithoutInternalDetails()
    {
        // Factory propria: quebra o banco para forcar excecao interna
        using var factory = new ApiFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Products\"");
        }

        var resp = await factory.CreateAdminClient().GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Products", body);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("relation", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body);
        Assert.Contains("traceId", body);
    }
}

public class HealthTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public HealthTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Healthz_ReturnsOk_WithoutDetails()
    {
        var resp = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await resp.Content.ReadAsStringAsync());
    }
}
