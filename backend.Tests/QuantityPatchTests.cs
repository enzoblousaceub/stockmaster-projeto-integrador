using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace backend.Tests;

public class QuantityPatchTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public QuantityPatchTests(ApiFactory factory) => _client = factory.CreateAdminClient();

    private async Task<ProductResponse> CreateAsync(int quantity)
    {
        var resp = await _client.PostAsJsonAsync("/api/products",
            new { name = "Patch", category = "Teste", quantity, price = 1, minStock = 0 });
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private Task<HttpResponseMessage> PatchAsync(int id, int delta) =>
        _client.PatchAsJsonAsync($"/api/products/{id}/quantity", new { delta });

    [Fact]
    public async Task Increment_And_Decrement_ReturnUpdatedProduct()
    {
        var p = await CreateAsync(10);

        var up = await PatchAsync(p.Id, 5);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var afterUp = await up.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(15, afterUp!.Quantity);
        Assert.Equal("Patch", afterUp.Name);
        Assert.True(afterUp.UpdatedAt >= p.UpdatedAt);

        var down = await PatchAsync(p.Id, -15);
        Assert.Equal(0, (await down.Content.ReadFromJsonAsync<ProductResponse>())!.Quantity);
    }

    [Fact]
    public async Task BelowZero_Returns409_AndKeepsQuantity()
    {
        var p = await CreateAsync(1);
        var resp = await PatchAsync(p.Id, -2);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);

        var current = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{p.Id}");
        Assert.Equal(1, current!.Quantity);
    }

    [Fact]
    public async Task AboveMaximum_Returns409()
    {
        var p = await CreateAsync(1_000_000);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchAsync(p.Id, 1)).StatusCode);
    }

    [Fact]
    public async Task UnknownProduct_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync(999999, 1)).StatusCode);
    }

    [Theory]
    [InlineData(1_000_001)]
    [InlineData(-1_000_001)]
    public async Task DeltaOutOfRange_Returns400(int delta)
    {
        var p = await CreateAsync(5);
        Assert.Equal(HttpStatusCode.BadRequest, (await PatchAsync(p.Id, delta)).StatusCode);
    }

    [Fact]
    public async Task EmptyObject_IsNoOp()
    {
        var p = await CreateAsync(5);
        var resp = await _client.PatchAsync($"/api/products/{p.Id}/quantity",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        // delta ausente vira 0 (no-op valido): quantidade nao muda
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(5, (await resp.Content.ReadFromJsonAsync<ProductResponse>())!.Quantity);
    }

    [Fact]
    public async Task ConcurrentIncrements_AreAllApplied()
    {
        var p = await CreateAsync(0);
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => PatchAsync(p.Id, 1)));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var current = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{p.Id}");
        Assert.Equal(40, current!.Quantity);
    }

    [Fact]
    public async Task ConcurrentDecrements_NeverGoBelowZero()
    {
        var p = await CreateAsync(10);
        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => PatchAsync(p.Id, -1)));
        Assert.Equal(10, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(15, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var current = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{p.Id}");
        Assert.Equal(0, current!.Quantity);
    }
}
