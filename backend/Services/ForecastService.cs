using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services
{
    public record ProductForecast(int ProductId, string Name, string Category, int Quantity, int MinStock,
        decimal Price, decimal Cost, int OutQuantity, int InQuantity, double AvgDailyOut, double? DaysToStockout,
        DateTime? StockoutDate, int SuggestedReorder, bool NeedsReorder);

    public record TopProduct(int ProductId, string Name, string Category, int OutQuantity, decimal Revenue, decimal Margin);

    public record StaleProduct(int ProductId, string Name, string Category, int Quantity, decimal ValueAtCost,
        DateTime? LastOutAt);

    public record DailyPoint(DateOnly Date, int In, int Out);

    public record ReportSummary(int Days, DateTime From, DateTime To, int InQuantity, int OutQuantity, int Adjustments,
        int MovementCount, decimal StockAtCost, decimal StockAtPrice, decimal PotentialMargin, decimal RevenueEstimate,
        decimal MarginEstimate, IReadOnlyList<DailyPoint> Daily, IReadOnlyList<ProductForecast> Forecast,
        IReadOnlyList<TopProduct> TopSellers, IReadOnlyList<StaleProduct> Stale);

    /// <summary>
    /// Relatorio de movimentacao e previsao (RF010/RF011) por estatistica simples:
    /// consumo medio diario = saidas no periodo / dias observados; ruptura = saldo / consumo;
    /// reposicao sugerida = consumo x dias de cobertura + estoque minimo - saldo.
    /// Ajustes (conferencia/edicao) nao contam como consumo.
    /// </summary>
    public class ForecastService
    {
        /// <summary>Dias de estoque que a sugestao de compra busca cobrir.</summary>
        public const int CoverageDays = 30;
        /// <summary>Alerta de ruptura quando o estoque acaba em ate N dias.</summary>
        public const int AlertDays = 14;

        private readonly AppDbContext _db;
        private readonly TimeSpan _offset;

        public ForecastService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            // fuso dos agrupamentos diarios (Brasilia, sem horario de verao desde 2019)
            _offset = TimeSpan.FromHours(config.GetValue("App:UtcOffsetHours", -3));
        }

        public async Task<ReportSummary> SummaryAsync(int days, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var from = now.AddDays(-days);

            var products = await _db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
            // venda desfeita nao e consumo: a saida e a entrada que a desfez saem das contas
            var valid = _db.StockMovements.AsNoTracking().Where(m => m.Sale == null || m.Sale.UndoneAt == null);
            var moves = await valid
                .Where(m => m.OccurredAt >= from && m.OccurredAt <= now)
                .Select(m => new { m.ProductId, m.Type, m.Delta, m.OccurredAt })
                .ToListAsync(ct);
            var lastOut = await valid
                .Where(m => m.Type == MovementTypes.Out && m.ProductId != null)
                .GroupBy(m => m.ProductId)
                .Select(g => new { ProductId = g.Key!.Value, Last = g.Max(m => m.OccurredAt) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Last, ct);

            var outBy = moves.Where(m => m.Type == MovementTypes.Out && m.ProductId != null)
                .GroupBy(m => m.ProductId!.Value).ToDictionary(g => g.Key, g => -g.Sum(m => m.Delta));
            var inBy = moves.Where(m => m.Type == MovementTypes.In && m.ProductId != null)
                .GroupBy(m => m.ProductId!.Value).ToDictionary(g => g.Key, g => g.Sum(m => m.Delta));

            var forecast = products.Select(p => Forecast(p, outBy.GetValueOrDefault(p.Id), inBy.GetValueOrDefault(p.Id), days, now))
                .OrderBy(f => f.DaysToStockout ?? double.MaxValue).ThenBy(f => f.Name)
                .ToList();

            var byId = products.ToDictionary(p => p.Id);
            var top = outBy.Where(kv => kv.Value > 0 && byId.ContainsKey(kv.Key))
                .Select(kv =>
                {
                    var p = byId[kv.Key];
                    return new TopProduct(p.Id, p.Name, p.Category, kv.Value, kv.Value * p.Price, kv.Value * (p.Price - p.Cost));
                })
                .OrderByDescending(t => t.OutQuantity).ThenBy(t => t.Name).Take(10).ToList();

            var stale = products.Where(p => p.Quantity > 0 && outBy.GetValueOrDefault(p.Id) == 0)
                .Select(p => new StaleProduct(p.Id, p.Name, p.Category, p.Quantity, p.Quantity * p.Cost,
                    lastOut.TryGetValue(p.Id, out var l) ? l : null))
                .OrderByDescending(s => s.ValueAtCost).ThenByDescending(s => s.Quantity).Take(10).ToList();

            // serie diaria no fuso local, com todos os dias do periodo (zeros incluidos)
            var local = (DateTime d) => DateOnly.FromDateTime(d.Add(_offset));
            var daily = moves.Where(m => m.Type != MovementTypes.Adjust)
                .GroupBy(m => local(m.OccurredAt))
                .ToDictionary(g => g.Key, g => (In: g.Where(m => m.Delta > 0).Sum(m => m.Delta), Out: -g.Where(m => m.Delta < 0).Sum(m => m.Delta)));
            var series = new List<DailyPoint>();
            for (var d = local(from); d <= local(now); d = d.AddDays(1))
            {
                var v = daily.GetValueOrDefault(d);
                series.Add(new DailyPoint(d, v.In, v.Out));
            }

            var stockAtCost = products.Sum(p => p.Quantity * p.Cost);
            var stockAtPrice = products.Sum(p => p.Quantity * p.Price);
            return new ReportSummary(days, from, now,
                InQuantity: inBy.Values.Sum(),
                OutQuantity: outBy.Values.Sum(),
                Adjustments: moves.Count(m => m.Type == MovementTypes.Adjust),
                MovementCount: moves.Count,
                StockAtCost: stockAtCost,
                StockAtPrice: stockAtPrice,
                PotentialMargin: stockAtPrice - stockAtCost,
                RevenueEstimate: outBy.Where(kv => byId.ContainsKey(kv.Key)).Sum(kv => kv.Value * byId[kv.Key].Price),
                MarginEstimate: outBy.Where(kv => byId.ContainsKey(kv.Key)).Sum(kv => kv.Value * (byId[kv.Key].Price - byId[kv.Key].Cost)),
                Daily: series,
                Forecast: forecast,
                TopSellers: top,
                Stale: stale);
        }

        public static ProductForecast Forecast(Product p, int outQty, int inQty, int days, DateTime now)
        {
            // produto cadastrado ha menos tempo que o periodo: divide so pelos dias em que existiu (minimo 1)
            var observed = Math.Clamp((now - p.CreatedAt).TotalDays, 1, days);
            var avg = outQty / observed;
            double? toStockout = avg > 0 ? p.Quantity / avg : null;
            var target = (int)Math.Ceiling(avg * CoverageDays) + p.MinStock;
            var suggestion = Math.Max(0, target - p.Quantity);
            var needs = p.Quantity <= p.MinStock || (toStockout is double t && t <= AlertDays);
            return new ProductForecast(p.Id, p.Name, p.Category, p.Quantity, p.MinStock, p.Price, p.Cost, outQty, inQty,
                Math.Round(avg, 2), toStockout is double v ? Math.Round(v, 1) : null,
                toStockout is double s ? now.AddDays(s) : null,
                needs ? suggestion : 0, needs && suggestion > 0);
        }
    }
}
