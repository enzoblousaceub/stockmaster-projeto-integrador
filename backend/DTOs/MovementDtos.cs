using System.ComponentModel.DataAnnotations;
using backend.Models;

namespace backend.DTOs
{
    /// <summary>
    /// Registro de movimentacao. type: "Entrada" | "Saida" | "Ajuste" (sem diferenciar maiusculas/acentos).
    /// Entrada/Saida: quantity = unidades movimentadas (&gt; 0). Ajuste (conferencia): quantity = saldo contado (&gt;= 0).
    /// </summary>
    public class CreateMovementDto
    {
        [Required, MaxLength(10)]
        public string Type { get; set; } = string.Empty;

        [Range(0, Limits.MaxValue)]
        public int Quantity { get; set; }

        /// <summary>Obrigatorio na saida (RF007). Ex.: Venda, Perda, Uso interno.</summary>
        [MaxLength(200)]
        public string? Reason { get; set; }

        /// <summary>Data do fato (RF006). Padrao: agora. Nao pode estar no futuro.</summary>
        public DateTimeOffset? OccurredAt { get; set; }
    }

    public record MovementDto(long Id, int? ProductId, string ProductName, string Type, int Delta, int QuantityAfter,
        string? Reason, DateTime OccurredAt, DateTime CreatedAt, string? CreatedBy)
    {
        public static MovementDto From(StockMovement m) => new(m.Id, m.ProductId, m.ProductName, m.Type, m.Delta,
            m.QuantityAfter, m.Reason, m.OccurredAt, m.CreatedAt, m.CreatedBy);
    }

    /// <summary>Resposta do registro: a movimentacao e o produto com o saldo atualizado.</summary>
    public record MovementResultDto(MovementDto Movement, Product Product);
}
