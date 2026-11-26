using backend.Data;
using backend.DTOs;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services
{
    public enum StockOutcome { Ok, NotFound, OutOfRange, NoChange }

    public record StockResult(StockOutcome Outcome, Product? Product = null, StockMovement? Movement = null);

    /// <summary>
    /// Unico ponto que altera o saldo de um produto por movimentacao. Trava a linha do produto (SELECT ... FOR UPDATE)
    /// dentro de uma transacao: cliques/registros concorrentes sao serializados e o saldo e o historico nunca divergem.
    /// </summary>
    public class StockService
    {
        private readonly AppDbContext _db;

        public StockService(AppDbContext db) => _db = db;

        /// <summary>Soma <paramref name="delta"/> ao saldo (entrada &gt; 0, saida &lt; 0, ajuste com sinal).</summary>
        public Task<StockResult> ApplyDeltaAsync(int productId, string type, int delta, string? reason, DateTime occurredAt,
            string? actor, CancellationToken ct) =>
            ApplyAsync(productId, type, current => (long)current + delta, reason, occurredAt, actor, ct);

        /// <summary>Define o saldo para o valor contado (conferencia/inventario) e registra a diferenca como Ajuste.</summary>
        public Task<StockResult> SetCountedAsync(int productId, int counted, string? reason, DateTime occurredAt,
            string? actor, CancellationToken ct) =>
            ApplyAsync(productId, MovementTypes.Adjust, _ => counted, reason, occurredAt, actor, ct);

        private async Task<StockResult> ApplyAsync(int productId, string type, Func<int, long> next, string? reason,
            DateTime occurredAt, string? actor, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var product = (await _db.Products
                .FromSqlInterpolated($"SELECT * FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
                .ToListAsync(ct)).FirstOrDefault();
            if (product is null) return new StockResult(StockOutcome.NotFound);

            var target = next(product.Quantity);
            if (target < 0 || target > Limits.MaxValue) return new StockResult(StockOutcome.OutOfRange, product);

            var delta = (int)(target - product.Quantity);
            if (delta == 0) return new StockResult(StockOutcome.NoChange, product);

            product.Quantity = (int)target;
            product.UpdatedAt = DateTime.UtcNow;
            var movement = NewMovement(product, type, delta, reason, occurredAt, actor);
            _db.StockMovements.Add(movement);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new StockResult(StockOutcome.Ok, product, movement);
        }

        /// <summary>Movimentacao para um produto ja rastreado (cadastro/edicao); quem chama faz o SaveChanges.</summary>
        public static StockMovement NewMovement(Product product, string type, int delta, string? reason, DateTime occurredAt,
            string? actor) => new()
        {
            Product = product,
            ProductId = product.Id == 0 ? null : product.Id,
            ProductName = product.Name,
            Type = type,
            Delta = delta,
            QuantityAfter = product.Quantity,
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
