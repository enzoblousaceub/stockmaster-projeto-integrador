using System.Net;
using System.Net.Http.Json;

namespace backend.Tests;

public class ValidationTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public ValidationTests(ApiFactory factory) => _client = factory.CreateAdminClient();

    private static object Body(int quantity = 1, decimal price = 1m, int minStock = 1,
        string name = "Item", string category = "Cat", string? description = null) =>
        new { name, description, category, quantity, price, minStock };

    [Theory]
    [InlineData(1_000_000, 1_000_000, 1_000_000)]
    [InlineData(0, 0, 0)]
    public async Task Create_AcceptsBoundaryValues(int quantity, int price, int minStock)
    {
        var resp = await _client.PostAsJsonAsync("/api/products", Body(quantity, price, minStock));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Theory]
    [InlineData(1_000_001, 1, 1)]
    [InlineData(1, 1_000_001, 1)]
    [InlineData(1, 1, 1_000_001)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 1, -1)]
    public async Task Create_RejectsOutOfRange(int quantity, int price, int minStock)
    {
        var resp = await _client.PostAsJsonAsync("/api/products", Body(quantity, price, minStock));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_RejectsOutOfRange()
    {
        var created = await (await _client.PostAsJsonAsync("/api/products", Body()))
            .Content.ReadFromJsonAsync<ProductResponse>();
        var resp = await _client.PutAsJsonAsync($"/api/products/{created!.Id}", Body(quantity: 2_000_000));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_RejectsTooLongFieldsAndWhitespaceName()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/products", Body(name: new string('a', 201)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/products", Body(description: new string('a', 501)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/products", Body(name: "   "))).StatusCode);
    }

    [Fact]
    public async Task Search_AndCategory_RejectOverLimitLength()
    {
        var longText = new string('a', 101);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/products?search={longText}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/products?category={longText}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/products?search={new string('a', 100)}")).StatusCode);
    }
}
