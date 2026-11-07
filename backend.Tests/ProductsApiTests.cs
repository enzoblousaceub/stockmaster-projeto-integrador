using System.Net;
using System.Net.Http.Json;

namespace backend.Tests;

public record ProductResponse(int Id, string Name, string? Description, string Category,
    int Quantity, decimal Price, int MinStock, DateTime CreatedAt, DateTime UpdatedAt);

public record StatsResponse(int TotalProducts, int TotalItems, decimal TotalValue,
    int LowStockCount, int OutOfStockCount, int Categories);

public class ProductsApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ProductsApiTests(ApiFactory factory) => _client = factory.CreateClient();

    private static object NewProduct(string name, string category = "Teste", int quantity = 5,
        decimal price = 10.5m, int minStock = 2) =>
        new { name, description = "desc", category, quantity, price, minStock };

    private async Task<ProductResponse> CreateAsync(string name, string category = "Teste", int quantity = 5)
    {
        var resp = await _client.PostAsJsonAsync("/api/products", NewProduct(name, category, quantity));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    [Fact]
    public async Task GetProducts_ReturnsSeededData()
    {
        var list = await _client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        Assert.NotNull(list);
        Assert.NotEmpty(list!);
    }

    [Fact]
    public async Task GetProducts_FiltersBySearchAndCategory()
    {
        var unique = $"Zeta-{Guid.NewGuid():N}";
        await CreateAsync(unique, "CatFiltro");

        var bySearch = await _client.GetFromJsonAsync<List<ProductResponse>>($"/api/products?search={unique.ToLower()}");
        Assert.Single(bySearch!);

        var byCategory = await _client.GetFromJsonAsync<List<ProductResponse>>("/api/products?category=catfiltro");
        Assert.Contains(byCategory!, p => p.Name == unique);
        Assert.All(byCategory!, p => Assert.Equal("catfiltro", p.Category.ToLower()));
    }

    [Fact]
    public async Task CreateProduct_ReturnsCreatedWithLocation()
    {
        var resp = await _client.PostAsJsonAsync("/api/products", NewProduct("Novo"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);
        var p = await resp.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.True(p!.Id > 0);
        Assert.Equal("Novo", p.Name);
    }

    [Fact]
    public async Task CreateProduct_RejectsMissingName()
    {
        var resp = await _client.PostAsJsonAsync("/api/products", NewProduct(""));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_RejectsNegativeQuantity()
    {
        var resp = await _client.PostAsJsonAsync("/api/products", NewProduct("X", quantity: -1));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GetProduct_NotFound_Returns404()
    {
        var resp = await _client.GetAsync("/api/products/999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateProduct_ChangesFields()
    {
        var created = await CreateAsync("Antes");
        var resp = await _client.PutAsJsonAsync($"/api/products/{created.Id}",
            NewProduct("Depois", "Outra", 9, 20m, 1));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var p = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{created.Id}");
        Assert.Equal("Depois", p!.Name);
        Assert.Equal(9, p.Quantity);
    }

    [Fact]
    public async Task UpdateProduct_NotFound_Returns404()
    {
        var resp = await _client.PutAsJsonAsync("/api/products/999999", NewProduct("X"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteProduct_RemovesIt()
    {
        var created = await CreateAsync("Apagar");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/products/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/products/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/products/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stats_AreConsistentWithProducts()
    {
        var list = await _client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        var stats = await _client.GetFromJsonAsync<StatsResponse>("/api/products/stats");
        Assert.Equal(list!.Count, stats!.TotalProducts);
        Assert.Equal(list.Sum(p => p.Quantity), stats.TotalItems);
        Assert.Equal(list.Count(p => p.Quantity == 0), stats.OutOfStockCount);
    }

    [Fact]
    public async Task Categories_AreDistinctAndSorted()
    {
        var cats = await _client.GetFromJsonAsync<List<string>>("/api/products/categories");
        Assert.Equal(cats!.Distinct().OrderBy(c => c).ToList(), cats);
    }
}
