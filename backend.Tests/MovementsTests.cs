using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace backend.Tests;

public record MovementResponse(long Id, int? ProductId, string ProductName, string Type, int Delta, int QuantityAfter,
    string? Reason, DateTime OccurredAt, DateTime CreatedAt, string? CreatedBy);

public record MovementResultResponse(MovementResponse Movement, ProductResponse Product);

public class MovementsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;

    public MovementsTests(ApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateAdminClient();
    }

    private async Task<ProductResponse> CreateAsync(int quantity, string? name = null)
    {
        var resp = await _admin.PostAsJsonAsync("/api/products",
            new { name = name ?? $"Mov-{Guid.NewGuid():N}"[..20], category = "Teste", quantity, price = 10, cost = 6, minStock = 2 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient c, int id, object body) =>
        c.PostAsJsonAsync($"/api/products/{id}/movements", body);

    private Task<List<MovementResponse>?> HistoryAsync(int id) =>
        _admin.GetFromJsonAsync<List<MovementResponse>>($"/api/products/{id}/movements");

    [Fact]
    public async Task Create_WithInitialQuantity_RecordsInitialEntry()
    {
        var p = await CreateAsync(7);
        var history = await HistoryAsync(p.Id);
        var m = Assert.Single(history!);
        Assert.Equal("Entrada", m.Type);
        Assert.Equal(7, m.Delta);
        Assert.Equal(7, m.QuantityAfter);
        Assert.Equal(ApiFactory.AdminUser, m.CreatedBy);
    }

    [Fact]
    public async Task Entry_IncreasesStock_WithDateAndReason()
    {
        var p = await CreateAsync(0);
        Assert.Empty((await HistoryAsync(p.Id))!);

        var when = DateTimeOffset.UtcNow.AddDays(-2);
        var resp = await MoveAsync(_admin, p.Id, new { type = "entrada", quantity = 12, reason = "Compra", occurredAt = when });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MovementResultResponse>();
        Assert.Equal(12, body!.Product.Quantity);
        Assert.Equal("Entrada", body.Movement.Type);
        Assert.Equal(12, body.Movement.Delta);
        Assert.Equal(12, body.Movement.QuantityAfter);
        Assert.Equal("Compra", body.Movement.Reason);
        Assert.True(Math.Abs((body.Movement.OccurredAt - when.UtcDateTime).TotalSeconds) < 1);
    }

    [Fact]
    public async Task Exit_DecreasesStock_AndRequiresReason()
    {
        var p = await CreateAsync(10);

        var noReason = await MoveAsync(_admin, p.Id, new { type = "Saida", quantity = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var problem = await noReason.ReadProblemAsync();
        Assert.Equal("validation_error", problem.Code);
        Assert.True(problem.Raw.GetProperty("errors").TryGetProperty("reason", out _));

        var ok = await MoveAsync(_admin, p.Id, new { type = "Saída", quantity = 3, reason = "Venda" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<MovementResultResponse>();
        Assert.Equal(7, body!.Product.Quantity);
        Assert.Equal("Saida", body.Movement.Type);
        Assert.Equal(-3, body.Movement.Delta);
    }

    [Fact]
    public async Task Exit_MoreThanStock_Returns409_AndKeepsStock()
    {
        var p = await CreateAsync(2);
        var resp = await MoveAsync(_admin, p.Id, new { type = "Saida", quantity = 3, reason = "Venda" });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal("insufficient_stock", (await resp.ReadProblemAsync()).Code);

        var current = await _admin.GetFromJsonAsync<ProductResponse>($"/api/products/{p.Id}");
        Assert.Equal(2, current!.Quantity);
        Assert.Single((await HistoryAsync(p.Id))!);
    }

    [Fact]
    public async Task Adjust_SetsCountedBalance_AndRecordsDifference()
    {
        var p = await CreateAsync(10);
        var resp = await MoveAsync(_admin, p.Id, new { type = "Ajuste", quantity = 8 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MovementResultResponse>();
        Assert.Equal(8, body!.Product.Quantity);
        Assert.Equal(-2, body.Movement.Delta);
        Assert.Equal("Conferencia de estoque", body.Movement.Reason);

        var same = await MoveAsync(_admin, p.Id, new { type = "Ajuste", quantity = 8 });
        Assert.Equal(HttpStatusCode.Conflict, same.StatusCode);
        Assert.Equal("no_change", (await same.ReadProblemAsync()).Code);
    }

    [Theory]
    [InlineData("transferencia", 1)]
    [InlineData("Entrada", 0)]
    [InlineData("Entrada", -1)]
    [InlineData("Entrada", 1_000_001)]
    public async Task InvalidInput_Returns400(string type, int quantity)
    {
        var p = await CreateAsync(1);
        var resp = await MoveAsync(_admin, p.Id, new { type, quantity, reason = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task FutureDate_Returns400()
    {
        var p = await CreateAsync(1);
        var resp = await MoveAsync(_admin, p.Id, new { type = "Entrada", quantity = 1, occurredAt = DateTimeOffset.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True((await resp.ReadProblemAsync()).Raw.GetProperty("errors").TryGetProperty("occurredAt", out _));
    }

    [Fact]
    public async Task UnknownProduct_Returns404()
    {
        var resp = await MoveAsync(_admin, 999999, new { type = "Entrada", quantity = 1 });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync("/api/products/999999/movements")).StatusCode);
    }

    [Fact]
    public async Task QuickPatch_AndEdit_AreRecordedAsAdjustments()
    {
        var p = await CreateAsync(5);
        Assert.Equal(HttpStatusCode.OK, (await _admin.PatchAsJsonAsync($"/api/products/{p.Id}/quantity", new { delta = -1 })).StatusCode);
        var put = await _admin.PutAsJsonAsync($"/api/products/{p.Id}",
            new { name = p.Name, category = "Teste", quantity = 9, price = 10, cost = 6, minStock = 2 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var history = (await HistoryAsync(p.Id))!;
        Assert.Equal(3, history.Count);
        Assert.Equal(new[] { 5, -1, 5 }, history.OrderBy(m => m.Id).Select(m => m.Delta));
        Assert.All(history.Where(m => m.Delta != 5 || m.Reason != "Estoque inicial"), m => Assert.Equal("Ajuste", m.Type));
        // saldo sempre igual a soma do historico
        Assert.Equal(9, history.Sum(m => m.Delta));
        Assert.Equal(9, history.OrderBy(m => m.Id).Last().QuantityAfter);
    }

    [Fact]
    public async Task ConcurrentExits_NeverOversell()
    {
        var p = await CreateAsync(5);
        var tasks = Enumerable.Range(0, 8).Select(_ => MoveAsync(_admin, p.Id, new { type = "Saida", quantity = 1, reason = "Venda" }));
        var results = await Task.WhenAll(tasks);
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var current = await _admin.GetFromJsonAsync<ProductResponse>($"/api/products/{p.Id}");
        Assert.Equal(0, current!.Quantity);
        var history = (await HistoryAsync(p.Id))!;
        Assert.Equal(0, history.Sum(m => m.Delta));
    }

    [Fact]
    public async Task DeletingProduct_KeepsHistory()
    {
        var name = $"Del-{Guid.NewGuid():N}"[..16];
        var p = await CreateAsync(4, name);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/products/{p.Id}")).StatusCode);

        var all = await _admin.GetFromJsonAsync<List<MovementResponse>>("/api/movements?limit=500");
        var kept = Assert.Single(all!, m => m.ProductName == name);
        Assert.Null(kept.ProductId);
    }

    [Fact]
    public async Task List_FiltersByType()
    {
        var p = await CreateAsync(3);
        await MoveAsync(_admin, p.Id, new { type = "Saida", quantity = 1, reason = "Perda" });
        var exits = await _admin.GetFromJsonAsync<List<MovementResponse>>("/api/movements?type=saida");
        Assert.NotEmpty(exits!);
        Assert.All(exits!, m => Assert.Equal("Saida", m.Type));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/movements?type=xyz")).StatusCode);
    }

    [Fact]
    public async Task Operator_MovesStock_ButCannotEditProducts()
    {
        var p = await CreateAsync(5);
        var op = await _factory.CreateUserClientAsync(_admin, $"op.{Guid.NewGuid():N}"[..12] + "@exemplo.com", "Operador");

        Assert.Equal(HttpStatusCode.Created, (await MoveAsync(op, p.Id, new { type = "Saida", quantity = 1, reason = "Venda" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.PatchAsJsonAsync($"/api/products/{p.Id}/quantity", new { delta = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/reports/summary")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync("/api/products",
            new { name = "x", category = "y", quantity = 1, price = 1, minStock = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.DeleteAsync($"/api/products/{p.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotMoveStock()
    {
        var p = await CreateAsync(5);
        var viewer = await _factory.CreateUserClientAsync(_admin, $"vw.{Guid.NewGuid():N}"[..12] + "@exemplo.com", "Visualizador");

        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(viewer, p.Id, new { type = "Entrada", quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/products/{p.Id}/movements")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        var (anon, _) = _factory.NewSession();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/movements")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/reports/summary")).StatusCode);
    }
}
