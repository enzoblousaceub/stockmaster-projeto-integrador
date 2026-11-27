using System.ComponentModel.DataAnnotations;

namespace backend.DTOs
{
    public class CreateProductDto
    {
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

        /// <summary>Saldo quando o produto nao tem grade (Variants nula). Com grade, e ignorado: o total e a soma.</summary>
        [Range(0, Limits.MaxValue)]
        public int Quantity { get; set; }

        [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
        public decimal Price { get; set; }

        /// <summary>Custo por unidade (opcional; padrao 0).</summary>
        [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
        public decimal Cost { get; set; }

        /// <summary>Minimo quando o produto nao tem grade (Variants nula). Com grade, cada variacao tem o seu.</summary>
        [Range(0, Limits.MaxValue)]
        public int MinStock { get; set; }

        /// <summary>
        /// Grade de tamanhos/materiais com o saldo de cada um. Nula = variacao unica com Quantity/MinStock (compatibilidade).
        /// </summary>
        public List<VariantInputDto>? Variants { get; set; }
    }
}
