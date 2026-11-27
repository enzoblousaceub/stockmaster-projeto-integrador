namespace backend.Models
{
    /// <summary>
    /// Variacao de um produto (ex.: tamanho 40, material Jeans) com saldo proprio.
    /// Todo produto tem pelo menos uma; produto sem grade usa a variacao unica <see cref="DefaultSize"/>.
    /// O saldo do produto (<see cref="Product.Quantity"/>) e sempre a soma dos saldos das variacoes.
    /// </summary>
    public class ProductVariant
    {
        public const string DefaultSize = "Único";
        public const int MaxSizeLength = 20;
        public const int MaxMaterialLength = 40;

        public int Id { get; set; }
        public int ProductId { get; set; }

        /// <summary>Tamanho/numeracao (ex.: 38, 40, P, M, G, Único).</summary>
        public string Size { get; set; } = DefaultSize;

        /// <summary>Material (ex.: Jeans, Sarja). Vazio quando o produto nao varia por material.</summary>
        public string Material { get; set; } = string.Empty;

        public int Quantity { get; set; }

        /// <summary>Estoque minimo desta variacao (alerta de reposicao).</summary>
        public int MinStock { get; set; }

        /// <summary>Ordem de exibicao definida no cadastro (a grade aparece na ordem em que foi montada).</summary>
        public int SortOrder { get; set; }

        /// <summary>Nome curto da variacao, gravado no historico (continua legivel se a variacao for removida).</summary>
        public string Label => MakeLabel(Size, Material);

        public static string MakeLabel(string size, string material) =>
            string.IsNullOrWhiteSpace(material) ? size : $"{size} · {material}";
    }

    public static class PaymentMethods
    {
        public const string Cash = "Dinheiro";
        public const string Pix = "Pix";
        public const string Debit = "Debito";
        public const string Credit = "Credito";

        /// <summary>Normaliza o que vem da API ("débito", "PIX", "credito"...). Null = invalido.</summary>
        public static string? Parse(string? raw)
        {
            var t = raw?.Trim().ToLowerInvariant().Replace('é', 'e');
            return t switch
            {
                "dinheiro" => Cash,
                "pix" => Pix,
                "debito" => Debit,
                "credito" => Credit,
                _ => null
            };
        }
    }

    /// <summary>
    /// Venda registrada no balcao: varios itens, uma forma de pagamento. Gera uma Saida por item.
    /// Pode ser desfeita por alguns minutos (o estoque volta com uma Entrada "Venda desfeita"; nada e apagado).
    /// </summary>
    public class Sale
    {
        public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(5);

        public long Id { get; set; }
        public string PaymentMethod { get; set; } = PaymentMethods.Cash;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public DateTime? UndoneAt { get; set; }
        public string? UndoneBy { get; set; }

        public List<StockMovement> Movements { get; set; } = new();
    }
}
