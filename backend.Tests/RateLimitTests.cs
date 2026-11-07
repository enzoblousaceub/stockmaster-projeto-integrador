using System.Net;
using System.Net.Http.Json;

namespace backend.Tests;

public class RateLimitTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RateLimitTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Writes_AreLimited_WithProblemDetailsAndRetryAfter()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:WritePerMinute", "3"));
        var client = factory.CreateClient();
        var body = new { name = "RL", category = "T", quantity = 1, price = 1, minStock = 0 };

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/products", body)).StatusCode);

        var limited = await client.PostAsJsonAsync("/api/products", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.True(limited.Headers.Contains("Retry-After"));

        // leitura tem balde proprio e continua liberada
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Reads_AreLimited_ButHealthzAndStaticAreNot()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:ReadPerMinute", "2"));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/products/stats")).StatusCode);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
    }
}
