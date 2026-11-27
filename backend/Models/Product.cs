using System.ComponentModel.DataAnnotations;
using backend.DTOs;

namespace backend.Models
{
    public class Product
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        /// <summary>Marca (opcional), ex.: Calvin Klein.</summary>
        [MaxLength(100)]
        public string? Brand { get; set; }

        /// <summary>Saldo total: soma das variacoes (mantido pelo backend, nunca editado direto).</summary>
        [Range(0, Limits.MaxValue)]
        public int Quantity { get; set; }

        [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
        public decimal Price { get; set; }

        /// <summary>Custo de aquisicao por unidade (RF001). Usado no valor do estoque a custo e na margem estimada.</summary>
        [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
        public decimal Cost { get; set; }

        /// <summary>Soma dos minimos das variacoes (mantido pelo backend; usado na previsao por produto).</summary>
        [Range(0, Limits.MaxValue)]
        public int MinStock { get; set; }

        /// <summary>Grade de tamanhos/materiais, cada uma com saldo proprio.</summary>
        public List<ProductVariant> Variants { get; set; } = new();

        /// <summary>Recalcula os totais a partir das variacoes.</summary>
        public void RecomputeTotals()
        {
            Quantity = Variants.Sum(v => v.Quantity);
            MinStock = Variants.Sum(v => v.MinStock);
        }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Quando a imagem foi trocada pela ultima vez (null = sem imagem). Serve tambem para invalidar cache no front.</summary>
        public DateTime? ImageUpdatedAt { get; set; }
    }
}
