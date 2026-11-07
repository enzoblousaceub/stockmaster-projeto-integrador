namespace backend.DTOs
{
    /// <summary>Limites de validacao compartilhados (mesmos valores do frontend).</summary>
    public static class Limits
    {
        public const int MaxValue = 1_000_000;      // Quantity, MinStock e Price: 0..1.000.000
        public const int MaxQueryLength = 100;      // search e category
    }
}
