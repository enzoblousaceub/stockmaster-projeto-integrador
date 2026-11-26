using backend.Models;

namespace backend.Data
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            if (context.Products.Any())
            {
                return; // O banco já foi inicializado
            }

            var now = DateTime.UtcNow;

            var products = new List<Product>
            {
                new()
                {
                    Name = "Mouse Sem Fio Ergonômico",
                    Description = "Mouse óptico sem fio ergonômico com receptor USB e ajuste de DPI",
                    Category = "Eletrônicos",
                    Quantity = 45,
                    Price = 89.90m,
                    Cost = 52.00m,
                    MinStock = 10,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    Name = "Teclado Mecânico RGB",
                    Description = "Teclado mecânico gamer RGB com switches azuis e padrão ABNT2",
                    Category = "Eletrônicos",
                    Quantity = 20,
                    Price = 249.90m,
                    Cost = 155.00m,
                    MinStock = 5,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    Name = "Hub USB-C 7 em 1",
                    Description = "Hub adaptador multifuncional USB-C com HDMI 4K, USB 3.0 e leitores SD",
                    Category = "Acessórios",
                    Quantity = 15,
                    Price = 139.90m,
                    Cost = 82.00m,
                    MinStock = 5,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    Name = "Suporte para Monitor Articulado",
                    Description = "Suporte articulado de mesa com pistão a gás para telas de 17 a 32 polegadas",
                    Category = "Móveis",
                    Quantity = 8,
                    Price = 179.90m,
                    Cost = 105.00m,
                    MinStock = 3,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    Name = "Webcam Full HD 1080p",
                    Description = "Câmera de alta resolução com microfone estéreo embutido para reuniões",
                    Category = "Eletrônicos",
                    Quantity = 12,
                    Price = 159.90m,
                    Cost = 96.00m,
                    MinStock = 4,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    Name = "Luminária de Mesa LED",
                    Description = "Luminária touch com temperatura de cor ajustável e porta USB de recarga",
                    Category = "Móveis",
                    Quantity = 4,
                    Price = 79.90m,
                    Cost = 41.00m,
                    MinStock = 5,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    Name = "Caderno Executivo A5",
                    Description = "Caderno pautado com capa dura texturizada, marcador de página e 160 folhas",
                    Category = "Escritório",
                    Quantity = 60,
                    Price = 34.90m,
                    Cost = 16.50m,
                    MinStock = 15,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    Name = "Cabo de Rede Cat6 3m",
                    Description = "Cabo de rede gigabit com conectores blindados e proteção anti-ruído",
                    Category = "Acessórios",
                    Quantity = 0,
                    Price = 19.90m,
                    Cost = 8.40m,
                    MinStock = 10,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            };

            context.Products.AddRange(products);
            context.SaveChanges();
            SeedHistory(context, products, now);
        }
    

        /// <summary>
        /// Historico de demonstracao (ultimos 45 dias): estoque inicial, vendas diarias e compras quinzenais,
        /// para relatorios e previsao terem dados. Deterministico (mesma semente) e coerente: o saldo final de cada
        /// produto e exatamente a soma das suas movimentacoes.
        /// </summary>
        private static void SeedHistory(AppDbContext context, List<Product> products, DateTime now)
        {
            var rng = new Random(42);
            const int days = 45;
            // vendas medias por dia de cada produto (mesma ordem da lista acima)
            var rates = new[] { 2.2, 0.9, 0.7, 0.25, 0.6, 0.35, 1.8, 1.1 };
            var reasons = new[] { "Venda", "Venda", "Venda", "Venda", "Perda", "Uso interno" };
            // um dia por iteracao a partir de 45 dias atras; o ultimo evento fica no maximo ~8h depois de "ontem a esta hora"
            var start = now.AddDays(-days);

            for (var i = 0; i < products.Count; i++)
            {
                var p = products[i];
                var rate = rates[i % rates.Length];
                var events = new List<(DateTime At, int Delta, string Reason)>();
                for (var d = 1; d < days; d++)
                {
                    var at = start.AddDays(d).AddMinutes(rng.Next(0, 480));
                    var sold = (int)Math.Round(rate + (rng.NextDouble() - 0.5) * rate);
                    if (sold > 0) events.Add((at, -sold, reasons[rng.Next(reasons.Length)]));
                    // reposicao a cada 15 dias, menos para o produto que termina esgotado
                    if (d % 15 == 0 && p.Quantity > 0) events.Add((at.AddHours(-3), (int)Math.Ceiling(rate * 15), "Compra de fornecedor"));
                }
                // nada no futuro (relogio de quem abre o sistema logo depois do seed)
                events.RemoveAll(e => e.At > now.AddMinutes(-5));

                // saldo inicial suficiente para nunca ficar negativo e terminar no saldo cadastrado
                var running = 0; var lowest = 0;
                foreach (var e in events) { running += e.Delta; lowest = Math.Min(lowest, running); }
                var initial = p.Quantity - running;
                if (initial + lowest < 0)
                {
                    // nao fecha: aumenta o estoque inicial e corrige o saldo final com uma conferencia
                    initial = -lowest;
                }

                var moves = new List<StockMovement>();
                var qty = initial;
                if (initial > 0)
                    moves.Add(new StockMovement { ProductId = p.Id, ProductName = p.Name, Type = MovementTypes.In, Delta = initial,
                        QuantityAfter = qty, Reason = "Estoque inicial", OccurredAt = start, CreatedAt = start, CreatedBy = "seed" });
                foreach (var e in events.OrderBy(e => e.At))
                {
                    qty += e.Delta;
                    moves.Add(new StockMovement { ProductId = p.Id, ProductName = p.Name,
                        Type = e.Delta > 0 ? MovementTypes.In : MovementTypes.Out, Delta = e.Delta, QuantityAfter = qty,
                        Reason = e.Reason, OccurredAt = e.At, CreatedAt = e.At, CreatedBy = "seed" });
                }
                if (qty != p.Quantity)
                {
                    var at = now.AddHours(-1);
                    moves.Add(new StockMovement { ProductId = p.Id, ProductName = p.Name, Type = MovementTypes.Adjust,
                        Delta = p.Quantity - qty, QuantityAfter = p.Quantity, Reason = "Conferencia de estoque",
                        OccurredAt = at, CreatedAt = at, CreatedBy = "seed" });
                }
                // historico existe ha 45 dias: a data de cadastro acompanha (a previsao usa os dias observados)
                p.CreatedAt = start;
                context.StockMovements.AddRange(moves);
            }
            context.SaveChanges();
        }
    }
}
