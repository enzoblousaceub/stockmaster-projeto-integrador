using System.ComponentModel.DataAnnotations;
using backend.Models;

namespace backend.DTOs
{
    /// <summary>
    /// Uma linha da grade no cadastro/edicao. Id preenchido = variacao existente (edita); sem Id = nova.
    /// Na edicao, variacoes existentes que nao vierem na lista sao removidas (o saldo que tinham sai como Ajuste).
    /// </summary>
    public class VariantInputDto
    {
        public int? Id { get; set; }

        [Required, MaxLength(ProductVariant.MaxSizeLength)]
        public string Size { get; set; } = string.Empty;

        [MaxLength(ProductVariant.MaxMaterialLength)]
        public string? Material { get; set; }

        [Range(0, Limits.MaxValue)]
        public int Quantity { get; set; }

        [Range(0, Limits.MaxValue)]
        public int MinStock { get; set; }
    }

    public static class VariantRules
    {
        public const int MaxVariants = 200;

        public static string NormSize(string? s) => (s ?? string.Empty).Trim();
        public static string NormMaterial(string? s) => (s ?? string.Empty).Trim();

        /// <summary>Erros da grade (vazia, linhas repetidas, tamanho em branco, total acima do limite). Vazio = ok.</summary>
        public static Dictionary<string, string> Validate(IReadOnlyList<VariantInputDto> variants)
        {
            var errors = new Dictionary<string, string>();
            if (variants.Count == 0) errors["variants"] = "Informe pelo menos um tamanho.";
            else if (variants.Count > MaxVariants) errors["variants"] = $"No maximo {MaxVariants} variacoes por produto.";
            else if (variants.Any(v => NormSize(v.Size).Length == 0)) errors["variants"] = "Todo item da grade precisa de um tamanho.";
            else
            {
                var dup = variants
                    .GroupBy(v => (NormSize(v.Size).ToLowerInvariant(), NormMaterial(v.Material).ToLowerInvariant()))
                    .FirstOrDefault(g => g.Count() > 1);
                if (dup is not null)
                    errors["variants"] = $"A variacao {ProductVariant.MakeLabel(NormSize(dup.First().Size), NormMaterial(dup.First().Material))} aparece mais de uma vez.";
                else if (variants.Sum(v => (long)v.Quantity) > Limits.MaxValue)
                    errors["variants"] = $"O estoque total do produto nao pode passar de {Limits.MaxValue}.";
                else if (variants.Where(v => v.Id is not null).GroupBy(v => v.Id).Any(g => g.Count() > 1))
                    errors["variants"] = "Variacao repetida na lista.";
            }
            return errors;
        }
    }
}
