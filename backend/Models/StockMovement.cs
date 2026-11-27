namespace backend.Models
{
    public static class MovementTypes
    {
        /// <summary>Entrada de mercadoria (compra, devolucao de cliente, estoque inicial...).</summary>
        public const string In = "Entrada";
        /// <summary>Saida de mercadoria (venda, perda, uso interno...). Conta como consumo na previsao.</summary>
        public const string Out = "Saida";
        /// <summary>Correcao de saldo (conferencia/inventario, edicao do produto). Nao conta como consumo.</summary>
        public const string Adjust = "Ajuste";
    }

    /// <summary>Historico de movimentacoes de estoque (RF006/RF007). Nunca e editado nem apagado pela API.</summary>
    public class StockMovement
    {
        public long Id { get; set; }

        /// <summary>Produto movimentado; fica nulo se o produto for excluido (o historico e mantido).</summary>
        public int? ProductId { get; set; }
        public Product? Product { get; set; }

        /// <summary>Nome do produto no momento da movimentacao (continua legivel apos exclusao/renomeacao).</summary>
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Variacao movimentada; fica nula se a variacao (ou o produto) for removida.</summary>
        public int? VariantId { get; set; }
        public ProductVariant? Variant { get; set; }

        /// <summary>Nome da variacao no momento (ex.: "40 · Jeans").</summary>
        public string? VariantLabel { get; set; }

        /// <summary>Venda a que a movimentacao pertence (saida da venda ou entrada que a desfez).</summary>
        public long? SaleId { get; set; }
        public Sale? Sale { get; set; }

        /// <summary>Forma de pagamento (so nas movimentacoes de venda).</summary>
        public string? PaymentMethod { get; set; }

        public string Type { get; set; } = MovementTypes.In;

        /// <summary>Variacao aplicada ao saldo: positiva na entrada, negativa na saida; no ajuste, o sinal indica o sentido.</summary>
        public int Delta { get; set; }

        /// <summary>Saldo do produto logo apos a movimentacao.</summary>
        public int QuantityAfter { get; set; }

        public string? Reason { get; set; }

        /// <summary>Data do fato (informada pelo usuario na entrada/saida; padrao: agora).</summary>
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        /// <summary>Quando foi registrada no sistema.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? CreatedBy { get; set; }
    }

    /// <summary>Imagem do produto (RF003), guardada no banco porque o disco do Render e efemero.</summary>
    public class ProductImage
    {
        public int ProductId { get; set; }
        public string ContentType { get; set; } = "image/jpeg";
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}
