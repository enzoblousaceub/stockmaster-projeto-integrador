using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using backend.Models;
using backend.Services;

namespace backend.Tests;

public class ReportsTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _admin;

    public ReportsTests(ApiFactory factory) => _admin = factory.CreateAdminClient();

    [Fact]
    public async Task Summary_ReturnsSeededHistory_WithForecast()
    {
        var json = await _admin.GetFromJsonAsync<JsonElement>("/api/reports/summary?days=30");
        Assert.Equal(30, json.GetProperty("days").GetInt32());
        Assert.True(json.GetProperty("outQuantity").GetInt32() > 0);
        Assert.True(json.GetProperty("stockAtCost").GetDecimal() > 0);
        Assert.True(json.GetProperty("stockAtPrice").GetDecimal() > json.GetProperty("stockAtCost").GetDecimal());
        Assert.InRange(json.GetProperty("daily").GetArrayLength(), 30, 32);
        Assert.NotEmpty(json.GetProperty("forecast").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("topSellers").EnumerateArray());
        // o produto esgotado do seed precisa de reposicao
        Assert.Contains(json.GetProperty("forecast").EnumerateArray(),
            f => f.GetProperty("quantity").GetInt32() == 0 && f.GetProperty("needsReorder").GetBoolean());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(366)]
    public async Task Summary_RejectsInvalidPeriod(int days)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"/api/reports/summary?days={days}")).StatusCode);
    }

}

/// <summary>Regras da previsao (sem banco).</summary>
public class ForecastRulesTests
{
    [Fact]
    public void Forecast_UsesAverageDailyConsumption()
    {
        var now = DateTime.UtcNow;
        var p = new Product { Id = 1, Name = "X", Category = "C", Quantity = 20, MinStock = 5, CreatedAt = now.AddDays(-60) };

        // 60 saidas em 30 dias = 2/dia -> acaba em 10 dias; alvo = 2*30 + 5 = 65 -> sugere 45
        var f = ForecastService.Forecast(p, outQty: 60, inQty: 0, days: 30, now);
        Assert.Equal(2, f.AvgDailyOut);
        Assert.Equal(10, f.DaysToStockout);
        Assert.True(f.NeedsReorder);
        Assert.Equal(45, f.SuggestedReorder);

        // sem consumo e acima do minimo: sem ruptura prevista nem sugestao
        var idle = ForecastService.Forecast(p, 0, 0, 30, now);
        Assert.Null(idle.DaysToStockout);
        Assert.False(idle.NeedsReorder);
        Assert.Equal(0, idle.SuggestedReorder);
    }

    [Fact]
    public void Forecast_NewProduct_DividesOnlyByObservedDays()
    {
        var now = DateTime.UtcNow;
        var p = new Product { Id = 1, Name = "Novo", Category = "C", Quantity = 30, MinStock = 0, CreatedAt = now.AddDays(-3) };
        var f = ForecastService.Forecast(p, outQty: 9, inQty: 0, days: 30, now);
        Assert.InRange(f.AvgDailyOut, 2.9, 3.1);
        Assert.InRange(f.DaysToStockout!.Value, 9.5, 10.5);
    }
}

public class ImageSnifferTests
{
    [Fact]
    public void Detects_SupportedFormats_AndRejectsOthers()
    {
        Assert.Equal("image/jpeg", ImageSniffer.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.Equal("image/png", ImageSniffer.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.Equal("image/webp", ImageSniffer.Detect("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(ImageSniffer.Detect("<svg/>"u8));
        Assert.Null(ImageSniffer.Detect("GIF89a"u8));
        Assert.Null(ImageSniffer.Detect(ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData("entrada", "Entrada")]
    [InlineData("SAÍDA", "Saida")]
    [InlineData(" saida ", "Saida")]
    [InlineData("Ajuste", "Ajuste")]
    [InlineData("venda", null)]
    [InlineData(null, null)]
    public void ParseType_NormalizesInput(string? raw, string? expected) =>
        Assert.Equal(expected, StockService.ParseType(raw));
}

public class ProductImageTests : IClassFixture<ApiFactory>
{
    private static readonly byte[] Png =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0
    };

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin;

    public ProductImageTests(ApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateAdminClient();
    }

    private async Task<int> CreateAsync()
    {
        var resp = await _admin.PostAsJsonAsync("/api/products", new { name = "Img", category = "Teste", quantity = 1, price = 1, cost = 0.5, minStock = 0 });
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static MultipartFormDataContent Form(byte[] data, string fileName = "foto.png", string contentType = "image/png")
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Upload_Get_Delete_RoundTrip()
    {
        var id = await CreateAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/products/{id}/image")).StatusCode);

        var put = await _admin.PutAsync($"/api/products/{id}/image", Form(Png));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var product = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, product.GetProperty("imageUpdatedAt").ValueKind);

        var get = await _admin.GetAsync($"/api/products/{id}/image");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await get.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/products/{id}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/products/{id}/image")).StatusCode);
        var after = await _admin.GetFromJsonAsync<JsonElement>($"/api/products/{id}");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("imageUpdatedAt").ValueKind);
    }

    [Fact]
    public async Task Upload_RejectsNonImage_EvenWithImageContentType()
    {
        var id = await CreateAsync();
        var resp = await _admin.PutAsync($"/api/products/{id}/image", Form("<svg onload=alert(1)>"u8.ToArray(), "x.png"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("validation_error", (await resp.ReadProblemAsync()).Code);
    }

    [Fact]
    public async Task Upload_RejectsTooLarge()
    {
        var id = await CreateAsync();
        var big = new byte[backend.Controllers.ProductsController.MaxImageBytes + 1];
        Png.CopyTo(big, 0);
        var resp = await _admin.PutAsync($"/api/products/{id}/image", Form(big));
        Assert.True(resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Viewer_CanSeeImage_ButCannotUpload()
    {
        var id = await CreateAsync();
        await _admin.PutAsync($"/api/products/{id}/image", Form(Png));
        var viewer = await _factory.CreateUserClientAsync(_admin, $"img.{Guid.NewGuid():N}"[..12] + "@exemplo.com", "Visualizador");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/products/{id}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsync($"/api/products/{id}/image", Form(Png))).StatusCode);
    }

    [Fact]
    public async Task Cost_IsSaved_AndStatsIncludeTotalCost()
    {
        var resp = await _admin.PostAsJsonAsync("/api/products", new { name = "Custo", category = "Teste", quantity = 2, price = 10, cost = 4.25, minStock = 0 });
        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4.25m, created.GetProperty("cost").GetDecimal());

        var stats = await _admin.GetFromJsonAsync<JsonElement>("/api/products/stats");
        Assert.True(stats.GetProperty("totalCost").GetDecimal() >= 8.5m);
    }
}
