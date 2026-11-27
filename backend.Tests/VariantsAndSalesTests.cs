using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.Tests;

public record VariantResponse(int Id, int ProductId, string Size, string Material, int Quantity, int MinStock, int SortOrder, string Label);

public record GridProductResponse(int Id, string Name, string? Brand, string Category, int Quantity, int MinStock,
    List<VariantResponse> Variants);

public record SaleItemResponse(int? ProductId, string ProductName, int? VariantId, string? VariantLabel, int Quantity);

public record SaleResponse(long Id, string PaymentMethod, DateTime CreatedAt, string? CreatedBy, DateTime? UndoneAt,
    string? UndoneBy, DateTime UndoDeadline, bool CanUndo, int TotalItems, List<SaleItemResponse> Items);

public record VariantMovementResponse(long Id, int? ProductId, string ProductName, int? VariantId, string? VariantLabel,
    string Type, int Delta, int QuantityAfter, string? Reason, long? SaleId, string? PaymentMethod);

/// <summary>Produtos com grade (tamanho x material), vendas do balcao e desfazer venda.</summary>
public class VariantsAndSalesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;

    public VariantsAndSalesTests(ApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateAdminClient();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    /// <summary>Calca com 38/40 em Jeans e 38 em Sarja.</summary>
    private async Task<GridProductResponse> CreateGridAsync(int q38 = 5, int q40 = 3, int qSarja = 2, string? name = null)
    {
        var resp = await _admin.PostAsJsonAsync("/api/products", new
        {
            name = name ?? Unique("Calca"),
            brand = "  Calvin Klein ",
            category = "Calças",
            price = 299.9m,
            cost = 150m,
            variants = new object[]
            {
                new { size = "38", material = "Jeans", quantity = q38, minStock = 2 },
                new { size = "40", material = "Jeans", quantity = q40, minStock = 1 },
                new { size = "38", material = "Sarja", quantity = qSarja, minStock = 1 },
            }
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<GridProductResponse>())!;
    }

    private Task<GridProductResponse?> GetAsync(int id) => _admin.GetFromJsonAsync<GridProductResponse>($"/api/products/{id}");

    private Task<List<VariantMovementResponse>?> HistoryAsync(int id) =>
        _admin.GetFromJsonAsync<List<VariantMovementResponse>>($"/api/products/{id}/movements");

    private static Task<HttpResponseMessage> SellAsync(HttpClient c, string payment, params (int VariantId, int Quantity)[] items) =>
        c.PostAsJsonAsync("/api/sales", new { paymentMethod = payment, items = items.Select(i => new { variantId = i.VariantId, quantity = i.Quantity }) });

    private Task<HttpClient> OperatorAsync() =>
        _factory.CreateUserClientAsync(_admin, $"op.{Guid.NewGuid():N}"[..12] + "@exemplo.com", "Operador");

    [Fact]
    public async Task Create_WithGrid_StoresVariants_TotalsAndInitialEntries()
    {
        var p = await CreateGridAsync();
        Assert.Equal("Calvin Klein", p.Brand);
        Assert.Equal(new[] { "38 · Jeans", "40 · Jeans", "38 · Sarja" }, p.Variants.Select(v => v.Label));
        Assert.Equal(10, p.Quantity);
        Assert.Equal(4, p.MinStock);

        var history = (await HistoryAsync(p.Id))!;
        Assert.Equal(3, history.Count);
        Assert.All(history, m => Assert.Equal("Estoque inicial", m.Reason));
        Assert.Equal(p.Variants.Select(v => v.Id).OrderBy(i => i), history.Select(m => m.VariantId!.Value).OrderBy(i => i));
    }

    [Fact]
    public async Task Create_WithoutGrid_GetsSingleVariant()
    {
        var resp = await _admin.PostAsJsonAsync("/api/products", new { name = Unique("Meia"), category = "Acessórios", quantity = 7, price = 10, minStock = 2 });
        var p = (await resp.Content.ReadFromJsonAsync<GridProductResponse>())!;
        var v = Assert.Single(p.Variants);
        Assert.Equal("Único", v.Size);
        Assert.Equal(7, v.Quantity);
        Assert.Equal(7, p.Quantity);
    }

    [Theory]
    [InlineData("40", "jeans ")]
    [InlineData(" 38", "Jeans")]
    public async Task Create_DuplicateVariant_Returns400(string size, string material)
    {
        var resp = await _admin.PostAsJsonAsync("/api/products", new
        {
            name = Unique("Dup"), category = "Calças", price = 1,
            variants = new object[] { new { size = "38", material = "Jeans" }, new { size = "40", material = "Jeans" }, new { size, material } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True((await resp.ReadProblemAsync()).Raw.GetProperty("errors").TryGetProperty("variants", out _));
    }

    [Fact]
    public async Task Create_EmptyGrid_Returns400()
    {
        var resp = await _admin.PostAsJsonAsync("/api/products", new { name = Unique("Vazio"), category = "x", price = 1, variants = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Grid_EditsAddsRemoves_AndRecordsEachChange()
    {
        var p = await CreateGridAsync(q38: 5, q40: 3, qSarja: 2);
        var v38 = p.Variants[0];
        var v40 = p.Variants[1];
        // 38 Jeans vai para 6, 40 Jeans fica, 38 Sarja sai (tinha 2), entra 42 Jeans com 4
        var put = await _admin.PutAsJsonAsync($"/api/products/{p.Id}", new
        {
            name = p.Name, category = "Calças", price = 299.9m, cost = 150m,
            variants = new object[]
            {
                new { id = v40.Id, size = "40", material = "Jeans", quantity = 3, minStock = 1 },
                new { id = v38.Id, size = "38", material = "Jeans", quantity = 6, minStock = 3 },
                new { size = "42", material = "Jeans", quantity = 4, minStock = 1 },
            }
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var after = (await GetAsync(p.Id))!;
        Assert.Equal(new[] { "40 · Jeans", "38 · Jeans", "42 · Jeans" }, after.Variants.Select(v => v.Label));
        Assert.Equal(13, after.Quantity);
        Assert.Equal(5, after.MinStock);
        Assert.Null(after.Brand);

        var history = (await HistoryAsync(p.Id))!;
        Assert.Contains(history, m => m.Reason == "Edicao do produto" && m.Delta == 1 && m.VariantId == v38.Id);
        Assert.Contains(history, m => m.Reason == "Variacao removida" && m.Delta == -2 && m.VariantLabel == "38 · Sarja" && m.VariantId == null);
        Assert.Contains(history, m => m.Reason == "Estoque inicial" && m.VariantLabel == "42 · Jeans" && m.Delta == 4);
        // saldo do produto sempre igual a soma do historico
        Assert.Equal(13, history.Sum(m => m.Delta));
    }

    /// <summary>Grade como a tela de edição envia: quantidade digitada + saldo que ela mostrava ao abrir.</summary>
    private static object[] EditGrid(GridProductResponse opened, Func<VariantResponse, int>? quantity = null) =>
        opened.Variants.Select(v => (object)new
        {
            id = v.Id, size = v.Size, material = v.Material, minStock = v.MinStock,
            quantity = quantity?.Invoke(v) ?? v.Quantity, expectedQuantity = v.Quantity
        }).ToArray();

    [Fact]
    public async Task Edit_WithoutTouchingStock_KeepsSaleMadeWhileFormWasOpen()
    {
        var opened = await CreateGridAsync(q38: 5, q40: 3, qSarja: 2);
        // a tela de edição abriu; nesse meio tempo o vendedor vendeu 2 do 38 Jeans
        Assert.Equal(HttpStatusCode.Created, (await SellAsync(_admin, "Pix", (opened.Variants[0].Id, 2))).StatusCode);

        // o dono só mudou o preço e salvou com os saldos que a tela mostrava
        var put = await _admin.PutAsJsonAsync($"/api/products/{opened.Id}",
            new { name = opened.Name, category = "Calças", price = 349.9m, variants = EditGrid(opened) });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var after = (await GetAsync(opened.Id))!;
        Assert.Equal(3, after.Variants[0].Quantity);   // a venda não foi apagada
        Assert.Equal(8, after.Quantity);
        Assert.DoesNotContain((await HistoryAsync(opened.Id))!, m => m.Reason == "Edicao do produto");
    }

    [Fact]
    public async Task Edit_ChangingStockThatMovedMeanwhile_Returns409_AndChangesNothing()
    {
        var opened = await CreateGridAsync(q38: 5, q40: 3, qSarja: 2);
        Assert.Equal(HttpStatusCode.Created, (await SellAsync(_admin, "Pix", (opened.Variants[0].Id, 2))).StatusCode);

        // o dono contou 7 no 38 Jeans com a tela desatualizada (mostrava 5; agora são 3)
        var put = await _admin.PutAsJsonAsync($"/api/products/{opened.Id}", new
        {
            name = "Nome novo", category = "Calças", price = 1,
            variants = EditGrid(opened, v => v.Id == opened.Variants[0].Id ? 7 : v.Quantity)
        });
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        var problem = await put.ReadProblemAsync();
        Assert.Equal("stock_changed", problem.Code);
        var conflict = Assert.Single(problem.Raw.GetProperty("conflicts").EnumerateArray());
        Assert.Equal(opened.Variants[0].Id, conflict.GetProperty("variantId").GetInt32());
        Assert.Equal(5, conflict.GetProperty("expected").GetInt32());
        Assert.Equal(3, conflict.GetProperty("current").GetInt32());

        var after = (await GetAsync(opened.Id))!;
        Assert.Equal(opened.Name, after.Name);
        Assert.Equal(3, after.Variants[0].Quantity);
    }

    [Fact]
    public async Task Edit_ChangingStockThatDidNotMove_IsSaved()
    {
        var opened = await CreateGridAsync(q38: 5, q40: 3, qSarja: 2);
        // outra variação vendeu: não impede corrigir o 38 Jeans
        await SellAsync(_admin, "Pix", (opened.Variants[1].Id, 1));
        var put = await _admin.PutAsJsonAsync($"/api/products/{opened.Id}", new
        {
            name = opened.Name, category = "Calças", price = 1,
            variants = EditGrid(opened, v => v.Id == opened.Variants[0].Id ? 9 : v.Quantity)
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var after = (await GetAsync(opened.Id))!;
        Assert.Equal(9, after.Variants[0].Quantity);
        Assert.Equal(2, after.Variants[1].Quantity);   // a venda do 40 continua
        Assert.Contains((await HistoryAsync(opened.Id))!, m => m.Reason == "Edicao do produto" && m.Delta == 4);
    }

    [Fact]
    public async Task Update_WithVariantOfAnotherProduct_Returns400()
    {
        var a = await CreateGridAsync();
        var b = await CreateGridAsync();
        var put = await _admin.PutAsJsonAsync($"/api/products/{a.Id}", new
        {
            name = a.Name, category = "Calças", price = 1,
            variants = new object[] { new { id = b.Variants[0].Id, size = "38", material = "Jeans", quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Movement_OnGridProduct_RequiresVariant_AndMovesOnlyIt()
    {
        var p = await CreateGridAsync(q38: 5, q40: 3);
        var noVariant = await _admin.PostAsJsonAsync($"/api/products/{p.Id}/movements", new { type = "Entrada", quantity = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, noVariant.StatusCode);

        var ok = await _admin.PostAsJsonAsync($"/api/products/{p.Id}/movements",
            new { type = "Entrada", variantId = p.Variants[1].Id, quantity = 2, reason = "Chegou mercadoria" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var after = (await GetAsync(p.Id))!;
        Assert.Equal(5, after.Variants[0].Quantity);
        Assert.Equal(5, after.Variants[1].Quantity);
        Assert.Equal(12, after.Quantity);

        var foreign = await CreateGridAsync();
        var wrong = await _admin.PostAsJsonAsync($"/api/products/{p.Id}/movements",
            new { type = "Entrada", variantId = foreign.Variants[0].Id, quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    }

    [Fact]
    public async Task Sale_WithSeveralItems_TakesAllFromStock_AndRecordsPayment()
    {
        var calca = await CreateGridAsync(q38: 5, q40: 3);
        var camiseta = await CreateGridAsync(q38: 10);
        var op = await OperatorAsync();

        // a mesma variacao duas vezes na sacola e somada
        var resp = await SellAsync(op, "débito", (calca.Variants[0].Id, 2), (camiseta.Variants[0].Id, 4), (calca.Variants[0].Id, 1));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var sale = (await resp.Content.ReadFromJsonAsync<SaleResponse>())!;
        Assert.Equal("Debito", sale.PaymentMethod);
        Assert.Equal(7, sale.TotalItems);
        Assert.Equal(2, sale.Items.Count);
        Assert.True(sale.CanUndo);

        Assert.Equal(2, (await GetAsync(calca.Id))!.Variants[0].Quantity);
        Assert.Equal(7, (await GetAsync(calca.Id))!.Quantity);
        Assert.Equal(6, (await GetAsync(camiseta.Id))!.Variants[0].Quantity);

        var m = (await HistoryAsync(calca.Id))!.First(x => x.SaleId == sale.Id);
        Assert.Equal("Saida", m.Type);
        Assert.Equal(-3, m.Delta);
        Assert.Equal("Venda", m.Reason);
        Assert.Equal("Debito", m.PaymentMethod);
        Assert.Equal("38 · Jeans", m.VariantLabel);
    }

    [Fact]
    public async Task Sale_WithOneItemShort_ChangesNothing()
    {
        var p = await CreateGridAsync(q38: 5, q40: 1);
        var resp = await SellAsync(_admin, "Pix", (p.Variants[0].Id, 2), (p.Variants[1].Id, 2));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.ReadProblemAsync();
        Assert.Equal("insufficient_stock", problem.Code);
        var shortage = Assert.Single(problem.Raw.GetProperty("shortages").EnumerateArray());
        Assert.Equal(p.Variants[1].Id, shortage.GetProperty("variantId").GetInt32());
        Assert.Equal(1, shortage.GetProperty("available").GetInt32());

        var after = (await GetAsync(p.Id))!;
        Assert.Equal(5, after.Variants[0].Quantity);
        Assert.Equal(1, after.Variants[1].Quantity);
        Assert.Equal(3, (await HistoryAsync(p.Id))!.Count);
    }

    [Theory]
    [InlineData("cheque", true)]
    [InlineData("", true)]
    [InlineData("Pix", false)]
    public async Task Sale_InvalidInput_Returns400(string payment, bool withItems)
    {
        var p = await CreateGridAsync();
        var items = withItems ? new[] { (p.Variants[0].Id, 1) } : Array.Empty<(int, int)>();
        var resp = await SellAsync(_admin, payment, items);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Sale_UnknownVariant_Returns409()
    {
        var resp = await SellAsync(_admin, "Pix", (999999, 1));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal("variant_not_found", (await resp.ReadProblemAsync()).Code);
    }

    [Fact]
    public async Task Undo_ReturnsStock_Once_AndOnlyForTheSeller()
    {
        var p = await CreateGridAsync(q38: 5);
        var op = await OperatorAsync();
        var other = await OperatorAsync();
        var sale = (await (await SellAsync(op, "Dinheiro", (p.Variants[0].Id, 3))).Content.ReadFromJsonAsync<SaleResponse>())!;
        Assert.Equal(2, (await GetAsync(p.Id))!.Variants[0].Quantity);

        // outro funcionario nem enxerga a venda
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/sales/{sale.Id}/undo", null)).StatusCode);
        Assert.DoesNotContain((await other.GetFromJsonAsync<List<SaleResponse>>("/api/sales"))!, s => s.Id == sale.Id);
        Assert.Contains((await op.GetFromJsonAsync<List<SaleResponse>>("/api/sales"))!, s => s.Id == sale.Id);

        var undo = await op.PostAsync($"/api/sales/{sale.Id}/undo", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        var undone = (await undo.Content.ReadFromJsonAsync<SaleResponse>())!;
        Assert.NotNull(undone.UndoneAt);
        Assert.False(undone.CanUndo);
        Assert.Equal(5, (await GetAsync(p.Id))!.Variants[0].Quantity);

        var history = (await HistoryAsync(p.Id))!;
        Assert.Contains(history, m => m.SaleId == sale.Id && m.Type == "Entrada" && m.Reason == "Venda desfeita" && m.Delta == 3);
        Assert.Equal((await GetAsync(p.Id))!.Quantity, history.Sum(m => m.Delta));

        var again = await op.PostAsync($"/api/sales/{sale.Id}/undo", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_undone", (await again.ReadProblemAsync()).Code);
    }

    [Fact]
    public async Task Undo_AfterWindow_Returns409()
    {
        var p = await CreateGridAsync(q38: 5);
        var sale = (await (await SellAsync(_admin, "Pix", (p.Variants[0].Id, 1))).Content.ReadFromJsonAsync<SaleResponse>())!;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Sales.Where(s => s.Id == sale.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddMinutes(-6)));
        }
        var resp = await _admin.PostAsync($"/api/sales/{sale.Id}/undo", null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal("undo_expired", (await resp.ReadProblemAsync()).Code);
        Assert.Equal(4, (await GetAsync(p.Id))!.Variants[0].Quantity);
    }

    [Fact]
    public async Task UndoneSale_DoesNotCountAsConsumption()
    {
        var p = await CreateGridAsync(q38: 5);
        var before = await _admin.GetFromJsonAsync<JsonElement>("/api/reports/summary?days=30");
        var sale = (await (await SellAsync(_admin, "Pix", (p.Variants[0].Id, 2))).Content.ReadFromJsonAsync<SaleResponse>())!;
        var during = await _admin.GetFromJsonAsync<JsonElement>("/api/reports/summary?days=30");
        Assert.Equal(before.GetProperty("outQuantity").GetInt32() + 2, during.GetProperty("outQuantity").GetInt32());

        await _admin.PostAsync($"/api/sales/{sale.Id}/undo", null);
        var after = await _admin.GetFromJsonAsync<JsonElement>("/api/reports/summary?days=30");
        Assert.Equal(before.GetProperty("outQuantity").GetInt32(), after.GetProperty("outQuantity").GetInt32());
        Assert.Equal(before.GetProperty("inQuantity").GetInt32(), after.GetProperty("inQuantity").GetInt32());
    }

    [Fact]
    public async Task ConcurrentSales_NeverOversell()
    {
        var p = await CreateGridAsync(q38: 4, q40: 4);
        var tasks = Enumerable.Range(0, 6).Select(i => SellAsync(_admin, "Pix", (p.Variants[0].Id, 1), (p.Variants[1].Id, 1)));
        var results = await Task.WhenAll(tasks);
        Assert.Equal(4, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var after = (await GetAsync(p.Id))!;
        Assert.Equal(0, after.Variants[0].Quantity);
        Assert.Equal(0, after.Variants[1].Quantity);
        Assert.Equal(2, after.Quantity);
    }

    [Fact]
    public async Task Operator_SellsAndReadsProducts_ButCannotManage()
    {
        var p = await CreateGridAsync();
        var op = await OperatorAsync();
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SellAsync(op, "Credito", (p.Variants[0].Id, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PutAsJsonAsync($"/api/products/{p.Id}", new { name = "x", category = "y", price = 1 })).StatusCode);

        var viewer = await _factory.CreateUserClientAsync(_admin, $"vw.{Guid.NewGuid():N}"[..12] + "@exemplo.com", "Visualizador");
        Assert.Equal(HttpStatusCode.Forbidden, (await SellAsync(viewer, "Pix", (p.Variants[0].Id, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/sales")).StatusCode);
    }

    [Fact]
    public async Task Alerts_ListVariantsAtOrBelowMinimum()
    {
        var p = await CreateGridAsync(q38: 0, q40: 1, qSarja: 9);
        var alerts = await _admin.GetFromJsonAsync<List<JsonElement>>("/api/products/alerts");
        var mine = alerts!.Where(a => a.GetProperty("productId").GetInt32() == p.Id).ToList();
        Assert.Equal(new[] { "38 · Jeans", "40 · Jeans" }, mine.Select(a => a.GetProperty("variantLabel").GetString()));
    }
}
