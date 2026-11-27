using System.ComponentModel.DataAnnotations;
using backend.Models;

namespace backend.DTOs
{
    /// <summary>
    /// Registro de movimentacao (so Admin). type: "Entrada" | "Saida" | "Ajuste" (sem diferenciar maiusculas/acentos).
    /// Entrada/Saida: quantity = unidades movimentadas (&gt; 0). Ajuste (conferencia): quantity = saldo contado (&gt;= 0).
    /// variantId: obrigatorio quando o produto tem mais de uma variacao.
    /// </summary>
    public class CreateMovementDto
    {
        [Required, MaxLength(10)]
        public string Type { get; set; } = string.Empty;

        public int? VariantId { get; set; }

        [Range(0, Limits.MaxValue)]
        public int Quantity { get; set; }

        /// <summary>Obrigatorio na saida (RF007). Ex.: Venda, Perda, Uso interno.</summary>
        [MaxLength(200)]
        public string? Reason { get; set; }

        /// <summary>Data do fato (RF006). Padrao: agora. Nao pode estar no futuro.</summary>
        public DateTimeOffset? OccurredAt { get; set; }
    }

    public record MovementDto(long Id, int? ProductId, string ProductName, int? VariantId, string? VariantLabel,
        string Type, int Delta, int QuantityAfter, string? Reason, DateTime OccurredAt, DateTime CreatedAt,
        string? CreatedBy, long? SaleId, string? PaymentMethod)
    {
        public static MovementDto From(StockMovement m) => new(m.Id, m.ProductId, m.ProductName, m.VariantId,
            m.VariantLabel, m.Type, m.Delta, m.QuantityAfter, m.Reason, m.OccurredAt, m.CreatedAt, m.CreatedBy,
            m.SaleId, m.PaymentMethod);
    }

    /// <summary>Resposta do registro: a movimentacao e o produto (com a grade) ja atualizado.</summary>
    public record MovementResultDto(MovementDto Movement, Product Product);

    public class SaleItemInputDto
    {
        public int VariantId { get; set; }

        [Range(1, Limits.MaxValue)]
        public int Quantity { get; set; }
    }

    /// <summary>Venda do balcao. paymentMethod: Dinheiro | Pix | Debito | Credito (sem diferenciar acentos).</summary>
    public class CreateSaleDto
    {
        [Required, MaxLength(10)]
        public string PaymentMethod { get; set; } = string.Empty;

        [Required]
        public List<SaleItemInputDto> Items { get; set; } = new();
    }

    public record SaleItemDto(int? ProductId, string ProductName, int? VariantId, string? VariantLabel, int Quantity);

    public record SaleDto(long Id, string PaymentMethod, DateTime CreatedAt, string? CreatedBy, DateTime? UndoneAt,
        string? UndoneBy, DateTime UndoDeadline, bool CanUndo, int TotalItems, IReadOnlyList<SaleItemDto> Items)
    {
        /// <summary><paramref name="outs"/>: as saidas da venda. CanUndo considera prazo e estado, nao o dono.</summary>
        public static SaleDto From(Sale s, IEnumerable<StockMovement> outs, DateTime now)
        {
            var items = outs.OrderBy(m => m.Id)
                .Select(m => new SaleItemDto(m.ProductId, m.ProductName, m.VariantId, m.VariantLabel, -m.Delta)).ToList();
            var deadline = s.CreatedAt + Sale.UndoWindow;
            return new SaleDto(s.Id, s.PaymentMethod, s.CreatedAt, s.CreatedBy, s.UndoneAt, s.UndoneBy, deadline,
                s.UndoneAt is null && now <= deadline, items.Sum(i => i.Quantity), items);
        }
    }
}
