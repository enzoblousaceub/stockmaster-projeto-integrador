using System.ComponentModel.DataAnnotations;

namespace backend.DTOs
{
    public class AdjustQuantityDto
    {
        /// <summary>Variacao a somar ao estoque (negativa para baixar). O resultado deve ficar entre 0 e 1.000.000.</summary>
        [Range(-Limits.MaxValue, Limits.MaxValue)]
        public int Delta { get; set; }
    }
}
