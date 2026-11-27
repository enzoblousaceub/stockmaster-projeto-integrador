using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services
{
    public enum StockOutcome { Ok, NotFound, OutOfRange, NoChange, VariantRequired, VariantNotFound }

    public record StockResult(StockOutcome Outcome, Product? Product = null, StockMovement? Movement = null,
        ProductVariant? Variant = null);

    public enum SaleOutcome { Ok, VariantNotFound, InsufficientStock, NotFound, AlreadyUndone, NotOwner, UndoExpired, OutOfRange }

    /// <summary>Falta de estoque numa venda: variacao, quanto foi pedido e quanto ha.</summary>
    public record Shortage(int VariantId, string ProductName, string VariantLabel, int Requested, int Available);

    public record SaleResult(SaleOutcome Outcome, Sale? Sale = null, IReadOnlyList<int>? MissingVariants = null,
        IReadOnlyList<Shortage>? Shortages = null);

    /// <summary>
    /// Unico ponto que altera saldos por movimentacao. Trava as linhas (SELECT ... FOR UPDATE) dentro de uma transacao,
    /// sempre na mesma ordem (variacoes por Id, depois produtos por Id): registros concorrentes sao serializados, sem
    /// deadlock, e saldo da variacao, saldo do produto e historico nunca divergem.
    /// </summary>
    public class StockService
    {
        private readonly AppDbContext _db;

        public StockService(AppDbContext db) => _db = db;

        /// <summary>Soma <paramref name="delta"/> ao saldo da variacao (entrada &gt; 0, saida &lt; 0, ajuste com sinal).
        /// Sem <paramref name="variantId"/>, so funciona para produto com uma unica variacao.</summary>
        public Task<StockResult> ApplyDeltaAsync(int productId, int? variantId, string type, int delta, string? reason,
            DateTime occurredAt, string? actor, CancellationToken ct) =>
            ApplyAsync(productId, variantId, type, current => (long)current + delta, reason, occurredAt, actor, ct);

        /// <summary>Define o saldo da variacao para o valor contado (conferencia) e registra a diferenca como Ajuste.</summary>
        public Task<StockResult> SetCountedAsync(int productId, int? variantId, int counted, string? reason,
            DateTime occurredAt, string? actor, CancellationToken ct) =>
            ApplyAsync(productId, variantId, MovementTypes.Adjust, _ => counted, reason, occurredAt, actor, ct);

        private async Task<StockResult> ApplyAsync(int productId, int? variantId, string type, Func<int, long> next,
            string? reason, DateTime occurredAt, string? actor, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var (product, variants) = await LockProductAsync(productId, ct);
            if (product is null) return new StockResult(StockOutcome.NotFound);

            ProductVariant? variant;
            if (variantId is int vid)
            {
                variant = variants.FirstOrDefault(v => v.Id == vid);
                if (variant is null) return new StockResult(StockOutcome.VariantNotFound, product);
            }
            else if (variants.Count == 1) variant = variants[0];
            else return new StockResult(StockOutcome.VariantRequired, product);

            var target = next(variant.Quantity);
            if (target < 0 || target > Limits.MaxValue || product.Quantity + (target - variant.Quantity) > Limits.MaxValue)
                return new StockResult(StockOutcome.OutOfRange, product, Variant: variant);

            var delta = (int)(target - variant.Quantity);
            if (delta == 0) return new StockResult(StockOutcome.NoChange, product, Variant: variant);

            variant.Quantity = (int)target;
            product.Quantity += delta;
            product.UpdatedAt = DateTime.UtcNow;
            var movement = NewMovement(product, variant, type, delta, reason, occurredAt, actor);
            _db.StockMovements.Add(movement);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new StockResult(StockOutcome.Ok, product, movement, variant);
        }

        /// <summary>Trava as variacoes do produto e depois o produto (mesma ordem de toda escrita). Use dentro de transacao.</summary>
        public async Task<(Product? Product, List<ProductVariant> Variants)> LockProductAsync(int productId, CancellationToken ct)
        {
            var variants = await _db.ProductVariants
                .FromSqlInterpolated($"SELECT * FROM \"ProductVariants\" WHERE \"ProductId\" = {productId} ORDER BY \"Id\" FOR UPDATE")
                .ToListAsync(ct);
            var product = (await _db.Products
                .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
                .ToListAsync(ct)).FirstOrDefault();
            // o EF associa as variacoes ja rastreadas a colecao do produto
            return (product, variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id).ToList());
        }

        /// <summary>
        /// Registra uma venda inteira numa transacao: ou todos os itens saem do estoque, ou nenhum.
        /// Itens repetidos da mesma variacao sao somados.
        /// </summary>
        public async Task<SaleResult> SellAsync(string paymentMethod, IEnumerable<(int VariantId, int Quantity)> items,
            string? actor, CancellationToken ct)
        {
            var wanted = items.GroupBy(i => i.VariantId).ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity));
            var ids = wanted.Keys.OrderBy(i => i).ToArray();

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var variants = await _db.ProductVariants
                .FromSqlInterpolated($"SELECT * FROM \"ProductVariants\" WHERE \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE")
                .ToListAsync(ct);
            var missing = ids.Except(variants.Select(v => v.Id)).ToList();
            if (missing.Count > 0) return new SaleResult(SaleOutcome.VariantNotFound, MissingVariants: missing);

            var productIds = variants.Select(v => v.ProductId).Distinct().OrderBy(i => i).ToArray();
            var products = (await _db.Products
                .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = ANY({productIds}) ORDER BY \"Id\" FOR UPDATE")
                .ToListAsync(ct)).ToDictionary(p => p.Id);

            var shortages = variants.Where(v => wanted[v.Id] > v.Quantity)
                .Select(v => new Shortage(v.Id, products[v.ProductId].Name, v.Label, (int)Math.Min(wanted[v.Id], int.MaxValue), v.Quantity))
                .ToList();
            if (shortages.Count > 0) return new SaleResult(SaleOutcome.InsufficientStock, Shortages: shortages);

            var now = DateTime.UtcNow;
            var sale = new Sale { PaymentMethod = paymentMethod, CreatedAt = now, CreatedBy = actor };
            _db.Sales.Add(sale);
            // ordem de exibicao: a mesma em que os itens foram adicionados na sacola
            foreach (var id in wanted.Keys)
            {
                var v = variants.First(x => x.Id == id);
                var p = products[v.ProductId];
                var qty = (int)wanted[id];
                v.Quantity -= qty;
                p.Quantity -= qty;
                p.UpdatedAt = now;
                var m = NewMovement(p, v, MovementTypes.Out, -qty, "Venda", now, actor);
                m.Sale = sale;
                m.PaymentMethod = paymentMethod;
                _db.StockMovements.Add(m);
            }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new SaleResult(SaleOutcome.Ok, sale);
        }

        /// <summary>
        /// Desfaz uma venda dentro de <see cref="Sale.UndoWindow"/>: devolve cada item ao estoque com uma Entrada
        /// "Venda desfeita" ligada a mesma venda. Nada e apagado. So quem registrou (ou um Admin) pode desfazer.
        /// Itens cuja variacao foi removida nesse meio tempo nao tem para onde voltar e sao ignorados.
        /// </summary>
        public async Task<SaleResult> UndoSaleAsync(long saleId, string? actor, bool isAdmin, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var sale = (await _db.Sales
                .FromSqlInterpolated($"SELECT * FROM \"Sales\" WHERE \"Id\" = {saleId} FOR UPDATE")
                .ToListAsync(ct)).FirstOrDefault();
            if (sale is null) return new SaleResult(SaleOutcome.NotFound);
            if (!isAdmin && !string.Equals(sale.CreatedBy, actor, StringComparison.OrdinalIgnoreCase))
                return new SaleResult(SaleOutcome.NotOwner, sale);
            if (sale.UndoneAt is not null) return new SaleResult(SaleOutcome.AlreadyUndone, sale);
            var now = DateTime.UtcNow;
            if (now - sale.CreatedAt > Sale.UndoWindow) return new SaleResult(SaleOutcome.UndoExpired, sale);

            var outs = await _db.StockMovements
                .Where(m => m.SaleId == saleId && m.Type == MovementTypes.Out)
                .OrderBy(m => m.Id).ToListAsync(ct);
            var ids = outs.Where(m => m.VariantId != null).Select(m => m.VariantId!.Value).Distinct().OrderBy(i => i).ToArray();
            var variants = (await _db.ProductVariants
                .FromSqlInterpolated($"SELECT * FROM \"ProductVariants\" WHERE \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE")
                .ToListAsync(ct)).ToDictionary(v => v.Id);
            var productIds = variants.Values.Select(v => v.ProductId).Distinct().OrderBy(i => i).ToArray();
            var products = (await _db.Products
                .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = ANY({productIds}) ORDER BY \"Id\" FOR UPDATE")
                .ToListAsync(ct)).ToDictionary(p => p.Id);

            foreach (var m in outs)
            {
                if (m.VariantId is not int vid || !variants.TryGetValue(vid, out var v)) continue;
                var p = products[v.ProductId];
                var qty = -m.Delta;
                if (v.Quantity + (long)qty > Limits.MaxValue || p.Quantity + (long)qty > Limits.MaxValue)
                    return new SaleResult(SaleOutcome.OutOfRange, sale);
                v.Quantity += qty;
                p.Quantity += qty;
                p.UpdatedAt = now;
                var back = NewMovement(p, v, MovementTypes.In, qty, "Venda desfeita", now, actor);
                back.SaleId = sale.Id;
                back.PaymentMethod = sale.PaymentMethod;
                _db.StockMovements.Add(back);
            }
            sale.UndoneAt = now;
            sale.UndoneBy = actor;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new SaleResult(SaleOutcome.Ok, sale);
        }

        /// <summary>Movimentacao para produto/variacao ja rastreados (cadastro/edicao); quem chama faz o SaveChanges.</summary>
        public static StockMovement NewMovement(Product product, ProductVariant variant, string type, int delta,
            string? reason, DateTime occurredAt, string? actor) => new()
        {
            Product = product,
            ProductId = product.Id == 0 ? null : product.Id,
            ProductName = product.Name,
            Variant = variant,
            VariantId = variant.Id == 0 ? null : variant.Id,
            VariantLabel = variant.Label,
            Type = type,
            Delta = delta,
            QuantityAfter = variant.Quantity,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OccurredAt = occurredAt,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actor
        };

        /// <summary>Normaliza o tipo vindo da API ("entrada", "SAÍDA", "saida"...). Null = invalido.</summary>
        public static string? ParseType(string? raw)
        {
            var t = raw?.Trim().ToLowerInvariant().Replace('í', 'i');
            return t switch
            {
                "entrada" => MovementTypes.In,
                "saida" => MovementTypes.Out,
                "ajuste" => MovementTypes.Adjust,
                _ => null
            };
        }
    }
}
