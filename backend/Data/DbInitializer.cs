using backend.Models;

namespace backend.Data
{
    /// <summary>
    /// Dados de demonstracao de uma loja de roupas: produtos com grade (tamanho x material), vendas do balcao com
    /// forma de pagamento nos ultimos 45 dias e compras de reposicao. Deterministico (mesma semente) e coerente:
    /// o saldo de cada variacao e exatamente a soma das suas movimentacoes.
    /// </summary>
    public static class DbInitializer
    {
        private record Seed(string Name, string Brand, string Category, string Description, decimal Price, decimal Cost,
            string[] Sizes, string[] Materials, double Popularity, bool Restock = true);

        private static readonly Seed[] Catalog =
        {
            new("Calça Jeans Slim Preta", "Calvin Klein", "Calças", "Modelagem slim, cintura média, lavagem escura.",
                299.90m, 150m, new[] { "38", "40", "42", "44", "46" }, new[] { "Jeans", "Sarja" }, 1.0),
            new("Calça Chino Bege", "Reserva", "Calças", "Corte reto em sarja com elastano, bolsos faca.",
                229.90m, 110m, new[] { "38", "40", "42", "44" }, new[] { "Sarja" }, 0.7),
            new("Camiseta Básica Branca", "Hering", "Camisetas", "Gola careca, malha 100% algodão.",
                49.90m, 22m, new[] { "P", "M", "G", "GG" }, new[] { "Algodão" }, 2.2),
            new("Camiseta Básica Preta", "Hering", "Camisetas", "Gola careca, malha 100% algodão.",
                49.90m, 22m, new[] { "P", "M", "G", "GG" }, new[] { "Algodão" }, 2.0),
            new("Camisa Polo Azul-Marinho", "Lacoste", "Camisetas", "Polo em malha piquet com bordado no peito.",
                389.90m, 190m, new[] { "P", "M", "G", "GG" }, new[] { "Piquet" }, 0.5),
            new("Camisa Social Branca", "Dudalina", "Camisas", "Manga longa, colarinho italiano.",
                349.90m, 170m, new[] { "P", "M", "G", "GG" }, new[] { "Algodão", "Linho" }, 0.6),
            new("Bermuda Jeans Clara", "Levi's", "Bermudas", "Lavagem clara, barra dobrada.",
                199.90m, 95m, new[] { "38", "40", "42", "44" }, new[] { "Jeans" }, 0.8),
            new("Jaqueta Jeans Trucker", "Levi's", "Jaquetas", "Clássica com botões de metal.",
                459.90m, 230m, new[] { "P", "M", "G" }, new[] { "Jeans" }, 0.5, Restock: false),
            new("Moletom Canguru Cinza", "Nike", "Moletons", "Capuz com cordão e bolso canguru.",
                329.90m, 160m, new[] { "P", "M", "G", "GG" }, new[] { "Moletom" }, 0.6),
            new("Vestido Midi Floral", "Farm", "Vestidos", "Estampa exclusiva, alças finas.",
                279.90m, 130m, new[] { "PP", "P", "M", "G" }, new[] { "Viscose", "Linho" }, 0.7),
            new("Meia Cano Alto", "Lupo", "Acessórios", "Pacote com 3 pares.",
                39.90m, 18m, new[] { ProductVariant.DefaultSize }, new[] { "" }, 1.5),
        };

        public static void Initialize(AppDbContext context)
        {
            if (context.Products.Any())
            {
                return; // O banco já foi inicializado
            }

            var now = DateTime.UtcNow;
            const int days = 45;
            var start = now.AddDays(-days);
            var rng = new Random(42);

            var products = new List<Product>();
            foreach (var s in Catalog)
            {
                var p = new Product
                {
                    Name = s.Name, Brand = s.Brand, Category = s.Category, Description = s.Description,
                    Price = s.Price, Cost = s.Cost, CreatedAt = start, UpdatedAt = start
                };
                var order = 0;
                foreach (var material in s.Materials)
                    foreach (var size in s.Sizes)
                    {
                        // tamanhos do meio vendem mais e tem minimo maior
                        var middle = Math.Abs(Array.IndexOf(s.Sizes, size) - (s.Sizes.Length - 1) / 2.0) < 1;
                        p.Variants.Add(new ProductVariant
                        {
                            Size = size, Material = material, SortOrder = order++,
                            MinStock = s.Popularity >= 1.5 ? 4 : middle ? 2 : 1
                        });
                    }
                products.Add(p);
            }
            context.Products.AddRange(products);
            context.SaveChanges();

            var moves = new List<StockMovement>();
            void Move(Product p, ProductVariant v, string type, int delta, string reason, DateTime at, string by, Sale? sale = null)
            {
                v.Quantity += delta;
                moves.Add(new StockMovement
                {
                    ProductId = p.Id, ProductName = p.Name, VariantId = v.Id, VariantLabel = v.Label, Type = type,
                    Delta = delta, QuantityAfter = v.Quantity, Reason = reason, OccurredAt = at, CreatedAt = at,
                    CreatedBy = by, Sale = sale, PaymentMethod = sale?.PaymentMethod
                });
            }

            // estoque inicial
            for (var i = 0; i < products.Count; i++)
                foreach (var v in products[i].Variants)
                    Move(products[i], v, MovementTypes.In,
                        (int)Math.Ceiling(Catalog[i].Popularity * 3) + v.MinStock + rng.Next(0, 4),
                        "Estoque inicial", start, "seed");

            // pesos de venda: produtos populares e tamanhos do meio saem mais
            var pool = new List<(int P, ProductVariant V, double W)>();
            for (var i = 0; i < products.Count; i++)
                foreach (var v in products[i].Variants)
                    pool.Add((i, v, Catalog[i].Popularity * (v.MinStock >= 2 ? 1.6 : 1.0) / Catalog[i].Materials.Length));
            var total = pool.Sum(x => x.W);
            (int P, ProductVariant V) Pick()
            {
                var r = rng.NextDouble() * total;
                foreach (var x in pool) { r -= x.W; if (r <= 0) return (x.P, x.V); }
                return (pool[^1].P, pool[^1].V);
            }
            var payments = new[] { PaymentMethods.Pix, PaymentMethods.Pix, PaymentMethods.Pix, PaymentMethods.Credit,
                PaymentMethods.Credit, PaymentMethods.Debit, PaymentMethods.Debit, PaymentMethods.Cash };
            var sellers = new[] { "seed", "seed" };

            var sales = new List<Sale>();
            for (var d = 1; d < days; d++)
            {
                var day = start.Date.AddDays(d);
                // reposicao quinzenal: completa o que esta no minimo
                if (d % 15 == 0)
                    for (var i = 0; i < products.Count; i++)
                        if (Catalog[i].Restock)
                            foreach (var v in products[i].Variants.Where(v => v.Quantity <= v.MinStock + 1))
                                Move(products[i], v, MovementTypes.In, v.MinStock * 3 + 3 - v.Quantity + 1,
                                    "Compra de fornecedor", day.AddHours(12), "seed");

                var count = rng.Next(2, 7) + (day.DayOfWeek is DayOfWeek.Saturday ? 3 : 0);
                for (var k = 0; k < count; k++)
                {
                    // horario de loja: 9h as 19h em Brasilia (12h a 22h UTC)
                    var at = day.AddHours(12).AddMinutes(rng.Next(0, 600));
                    if (at > now.AddMinutes(-5)) continue;
                    var items = new Dictionary<ProductVariant, (int P, int Q)>();
                    var lines = rng.NextDouble() < 0.65 ? 1 : rng.Next(2, 4);
                    for (var l = 0; l < lines; l++)
                    {
                        var (pi, v) = Pick();
                        var already = items.TryGetValue(v, out var e) ? e.Q : 0;
                        var q = rng.NextDouble() < 0.8 ? 1 : 2;
                        if (v.Quantity - already >= q) items[v] = (pi, already + q);
                    }
                    if (items.Count == 0) continue;
                    var sale = new Sale { PaymentMethod = payments[rng.Next(payments.Length)], CreatedAt = at, CreatedBy = sellers[k % sellers.Length] };
                    sales.Add(sale);
                    foreach (var (v, (pi, q)) in items)
                        Move(products[pi], v, MovementTypes.Out, -q, "Venda", at, sale.CreatedBy!, sale);
                }
            }

            // produto sem reposicao termina esgotado (aparece na reposicao sugerida)
            var conf = now.AddHours(-1);
            for (var i = 0; i < products.Count; i++)
                if (!Catalog[i].Restock)
                    foreach (var v in products[i].Variants.Where(v => v.Quantity > 0))
                        Move(products[i], v, MovementTypes.Adjust, -v.Quantity, "Conferencia de estoque", conf, "seed");

            foreach (var p in products)
            {
                p.RecomputeTotals();
                p.UpdatedAt = now;
            }
            context.Sales.AddRange(sales);
            context.StockMovements.AddRange(moves);
            context.SaveChanges();
        }
    }
}
