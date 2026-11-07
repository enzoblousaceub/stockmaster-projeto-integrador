using System.ComponentModel.DataAnnotations;

namespace backend.DTOs
{
    public class UpdateProductDto
    {
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        [Range(0, Limits.MaxValue)]
        public int Quantity { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal Price { get; set; }

        [Range(0, Limits.MaxValue)]
        public int MinStock { get; set; }
    }
}
