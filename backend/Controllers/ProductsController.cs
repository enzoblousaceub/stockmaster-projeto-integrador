using backend.Data;
using backend.DTOs;
using backend.Models;
using System.ComponentModel.DataAnnotations;
using backend.Security;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        public const int MaxImageBytes = 2 * 1024 * 1024;

        private readonly AppDbContext _context;
        private readonly StockService _stock;
        private readonly AuditService _audit;

        public ProductsController(AppDbContext context, StockService stock, AuditService audit)
        {
            _context = context;
            _stock = stock;
            _audit = audit;
        }

        private string? Actor => User.Identity?.Name;

        /// <summary>Identificacao curta do produto para a auditoria (mesmo codigo exibido no front, RF002).</summary>
        private static string Label(Product p) => $"#{p.Id:D4} {p.Name}";

        /// <summary>Produtos com a grade em ordem de cadastro.</summary>
        private IQueryable<Product> WithVariants() =>
            _context.Products.Include(p => p.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id));

        // GET: api/products
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts([FromQuery, MaxLength(Limits.MaxQueryLength)] string? search, [FromQuery, MaxLength(Limits.MaxQueryLength)] string? category)
        {
            var query = WithVariants().AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{EscapeLike(search.Trim())}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, pattern, "\\") ||
                    (p.Brand != null && EF.Functions.ILike(p.Brand, pattern, "\\")) ||
                    (p.Description != null && EF.Functions.ILike(p.Description, pattern, "\\")));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(p => EF.Functions.ILike(p.Category, EscapeLike(category.Trim()), "\\"));
            }

            return await query.OrderByDescending(p => p.UpdatedAt).ToListAsync();
        }

        // Escapa curingas do LIKE (% _ \) para que a busca seja literal
        private static string EscapeLike(string value) =>
            value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        // GET: api/products/stats  (alertas contados por variacao: "40 Jeans acabando" e um alerta)
        [HttpGet("stats")]
        public async Task<ActionResult<object>> GetStats()
        {
            var products = await _context.Products.AsNoTracking().ToListAsync();
            var variants = await _context.ProductVariants.AsNoTracking().ToListAsync();
            var totalProducts = products.Count;
            var totalItems = products.Sum(p => (long)p.Quantity);
            var totalValue = products.Sum(p => p.Quantity * p.Price);
            var totalCost = products.Sum(p => p.Quantity * p.Cost);
            var lowStockCount = variants.Count(v => v.Quantity > 0 && v.Quantity <= v.MinStock);
            var outOfStockCount = variants.Count(v => v.Quantity == 0);
            var categories = products.Select(p => p.Category).Distinct().Count();

            return Ok(new
            {
                totalProducts,
                totalItems,
                totalValue,
                totalCost,
                lowStockCount,
                outOfStockCount,
                categories
            });
        }

        /// <summary>Variação cujo saldo mudou entre abrir e salvar a edição.</summary>
        public record StockConflict(int VariantId, string VariantLabel, int Expected, int Current);

        public record StockAlert(int ProductId, string ProductName, string? Brand, string Category, DateTime? ImageUpdatedAt,
            int VariantId, string VariantLabel, int Quantity, int MinStock);

        // GET: api/products/alerts  (variacoes zeradas ou no minimo, zeradas primeiro)
        [HttpGet("alerts")]
        public async Task<ActionResult<IEnumerable<StockAlert>>> GetAlerts(CancellationToken ct)
        {
            var rows = await (from v in _context.ProductVariants.AsNoTracking()
                              join p in _context.Products.AsNoTracking() on v.ProductId equals p.Id
                              where v.Quantity == 0 || v.Quantity <= v.MinStock
                              select new { p, v }).ToListAsync(ct);
            return Ok(rows
                .OrderBy(r => r.v.Quantity > 0).ThenBy(r => r.p.Name).ThenBy(r => r.v.SortOrder)
                .Select(r => new StockAlert(r.p.Id, r.p.Name, r.p.Brand, r.p.Category, r.p.ImageUpdatedAt, r.v.Id,
                    r.v.Label, r.v.Quantity, r.v.MinStock)));
        }

        // GET: api/products/categories
        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<string>>> GetCategories()
        {
            var categories = await _context.Products
                .Select(p => p.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return Ok(categories);
        }

        // GET: api/products/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(int id)
        {
            var product = await WithVariants().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found", detail: $"Product with ID {id} not found.");
            }

            return product;
        }

        /// <summary>Grade enviada, ou a variacao unica de compatibilidade (Quantity/MinStock) quando nao vier.</summary>
        private static List<VariantInputDto> GridOf(List<VariantInputDto>? variants, int quantity, int minStock) =>
            variants is null
                ? new() { new VariantInputDto { Size = ProductVariant.DefaultSize, Quantity = quantity, MinStock = minStock } }
                : variants;

        private ActionResult VariantErrors(Dictionary<string, string> errors)
        {
            foreach (var (k, v) in errors) ModelState.AddModelError(k, v);
            var pd = ProblemDetailsFactory.CreateValidationProblemDetails(HttpContext, ModelState);
            pd.Extensions["code"] = "validation_error";
            return new BadRequestObjectResult(pd) { ContentTypes = { "application/problem+json" } };
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // POST: api/products
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPost]
        public async Task<ActionResult<Product>> CreateProduct(CreateProductDto dto)
        {
            var grid = GridOf(dto.Variants, dto.Quantity, dto.MinStock);
            var errors = VariantRules.Validate(grid);
            if (grid.Any(v => v.Id is not null)) errors["variants"] = "Produto novo nao tem variacoes existentes.";
            if (errors.Count > 0) return VariantErrors(errors);

            var now = DateTime.UtcNow;
            var product = new Product
            {
                Name = dto.Name,
                Description = dto.Description,
                Category = dto.Category,
                Brand = Clean(dto.Brand),
                Price = dto.Price,
                Cost = dto.Cost,
                CreatedAt = now,
                UpdatedAt = now
            };
            for (var i = 0; i < grid.Count; i++)
            {
                var g = grid[i];
                product.Variants.Add(new ProductVariant
                {
                    Size = VariantRules.NormSize(g.Size),
                    Material = VariantRules.NormMaterial(g.Material),
                    Quantity = g.Quantity,
                    MinStock = g.MinStock,
                    SortOrder = i
                });
            }
            product.RecomputeTotals();

            _context.Products.Add(product);
            // saldo inicial de cada variacao entra no historico: o saldo sempre bate com a soma das movimentacoes
            foreach (var v in product.Variants.Where(v => v.Quantity > 0))
                _context.StockMovements.Add(StockService.NewMovement(product, v, MovementTypes.In, v.Quantity,
                    "Estoque inicial", now, Actor));
            await _context.SaveChangesAsync();
            await _audit.LogAsync("produto_criado", "ok", Actor, null, Label(product));

            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }

        // PUT: api/products/5
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, UpdateProductDto dto, CancellationToken ct)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            var (product, variants) = await _stock.LockProductAsync(id, ct);

            if (product == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found", detail: $"Product with ID {id} not found.");
            }

            List<VariantInputDto> grid;
            if (dto.Variants is not null) grid = dto.Variants;
            else if (variants.Count == 1)
            {
                // compatibilidade: sem grade, Quantity/MinStock valem para a variacao unica
                var only = variants[0];
                grid = new() { new VariantInputDto { Id = only.Id, Size = only.Size, Material = only.Material, Quantity = dto.Quantity, MinStock = dto.MinStock } };
            }
            else grid = variants.Select(v => new VariantInputDto { Id = v.Id, Size = v.Size, Material = v.Material, Quantity = v.Quantity, MinStock = v.MinStock }).ToList();

            var errors = VariantRules.Validate(grid);
            if (grid.Any(g => g.Id is int gid && variants.All(v => v.Id != gid)))
                errors["variants"] = "Variacao nao pertence a este produto.";
            if (errors.Count > 0) return VariantErrors(errors);

            // saldo que a tela mostrava x saldo atual (linhas travadas): nunca sobrescreve venda feita no meio tempo
            var conflicts = new List<StockConflict>();
            foreach (var g in grid)
            {
                if (g.Id is not int gid || g.ExpectedQuantity is not int expected) continue;
                var current = variants.First(v => v.Id == gid);
                if (g.Quantity == expected) g.Quantity = current.Quantity;        // não mexida: mantém o saldo atual
                else if (current.Quantity != expected)
                    conflicts.Add(new StockConflict(gid, current.Label, expected, current.Quantity));
            }
            if (conflicts.Count > 0)
            {
                var pd = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status409Conflict,
                    "Estoque mudou", conflicts.Count == 1
                        ? $"O saldo de {conflicts[0].VariantLabel} mudou de {conflicts[0].Expected} para {conflicts[0].Current} enquanto a tela estava aberta."
                        : $"{conflicts.Count} tamanhos mudaram de saldo enquanto a tela estava aberta.");
                pd.Extensions["code"] = "stock_changed";
                pd.Extensions["conflicts"] = conflicts;
                return new ConflictObjectResult(pd) { ContentTypes = { "application/problem+json" } };
            }
            if (grid.Sum(g => (long)g.Quantity) > Limits.MaxValue)
                return VariantErrors(new Dictionary<string, string> { ["variants"] = $"O estoque total do produto nao pode passar de {Limits.MaxValue}." });

            var now = DateTime.UtcNow;
            product.Name = dto.Name;
            product.Description = dto.Description;
            product.Category = dto.Category;
            product.Brand = Clean(dto.Brand);
            product.Price = dto.Price;
            product.Cost = dto.Cost;
            product.UpdatedAt = now;

            // removidas: o saldo que tinham sai como ajuste e o historico guarda o nome da variacao
            var keep = grid.Where(g => g.Id is not null).Select(g => g.Id!.Value).ToHashSet();
            foreach (var gone in variants.Where(v => !keep.Contains(v.Id)))
            {
                if (gone.Quantity != 0)
                {
                    var removed = -gone.Quantity;
                    gone.Quantity = 0;
                    _context.StockMovements.Add(StockService.NewMovement(product, gone, MovementTypes.Adjust, removed,
                        "Variacao removida", now, Actor));
                }
                product.Variants.Remove(gone);
                _context.ProductVariants.Remove(gone);
            }

            for (var i = 0; i < grid.Count; i++)
            {
                var g = grid[i];
                var v = g.Id is int gid ? variants.First(x => x.Id == gid) : null;
                var isNew = v is null;
                if (v is null)
                {
                    v = new ProductVariant { ProductId = product.Id };
                    product.Variants.Add(v);
                }
                v.Size = VariantRules.NormSize(g.Size);
                v.Material = VariantRules.NormMaterial(g.Material);
                v.MinStock = g.MinStock;
                v.SortOrder = i;
                var delta = g.Quantity - v.Quantity;
                v.Quantity = g.Quantity;
                // mudanca de saldo pela edicao fica registrada (o caminho normal e /movements); variacao nova entra como estoque inicial
                if (delta != 0)
                    _context.StockMovements.Add(StockService.NewMovement(product, v,
                        isNew ? MovementTypes.In : MovementTypes.Adjust, delta,
                        isNew ? "Estoque inicial" : "Edicao do produto", now, Actor));
            }
            product.RecomputeTotals();

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await _audit.LogAsync("produto_editado", "ok", Actor, null, Label(product));

            product.Variants = product.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id).ToList();
            return Ok(product);
        }

        // PATCH: api/products/5/quantity  {"delta": -1, "variantId": 12}
        // Ajuste rapido (botoes +/-): registrado como movimentacao do tipo Ajuste. Atomico (linhas travadas na transacao).
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPatch("{id}/quantity")]
        public async Task<ActionResult<Product>> AdjustQuantity(int id, AdjustQuantityDto dto, CancellationToken ct)
        {
            var result = await _stock.ApplyDeltaAsync(id, dto.VariantId, MovementTypes.Adjust, dto.Delta, "Ajuste rapido",
                DateTime.UtcNow, Actor, ct);

            return result.Outcome switch
            {
                StockOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found",
                    detail: $"Product with ID {id} not found."),
                StockOutcome.OutOfRange => Problem(statusCode: StatusCodes.Status409Conflict, title: "Invalid stock quantity",
                    detail: $"Quantity must stay between 0 and {Limits.MaxValue}."),
                StockOutcome.VariantRequired or StockOutcome.VariantNotFound => VariantErrors(new Dictionary<string, string>
                    { ["variantId"] = result.Outcome == StockOutcome.VariantRequired ? "Escolha o tamanho/material." : "Variacao nao pertence a este produto." }),
                _ => Ok(await WithVariants().AsNoTracking().FirstAsync(p => p.Id == id, ct))
            };
        }

        // DELETE: api/products/5
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found", detail: $"Product with ID {id} not found.");
            }

            var label = Label(product);
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            await _audit.LogAsync("produto_excluido", "ok", Actor, null, label);

            return NoContent();
        }

        // GET: api/products/5/image
        // Exige sessao (politica padrao). A URL usada pelo front leva ?v=<ImageUpdatedAt>, entao pode ficar em cache.
        [HttpGet("{id}/image")]
        public async Task<IActionResult> GetImage(int id, CancellationToken ct)
        {
            var image = await _context.ProductImages.AsNoTracking().FirstOrDefaultAsync(i => i.ProductId == id, ct);
            if (image is null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Image not found", detail: $"Product {id} has no image.");

            Response.Headers.CacheControl = "private, max-age=86400";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(image.Data, image.ContentType);
        }

        // PUT: api/products/5/image  (multipart/form-data, campo "file"; JPEG, PNG ou WebP ate 2 MB)
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPut("{id}/image")]
        [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxImageBytes + 64 * 1024)]
        public async Task<ActionResult<Product>> PutImage(int id, IFormFile? file, CancellationToken ct)
        {
            var product = await _context.Products.FindAsync(new object[] { id }, ct);
            if (product == null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found", detail: $"Product with ID {id} not found.");

            if (file is null || file.Length == 0)
                return FileError("Envie uma imagem.");
            if (file.Length > MaxImageBytes)
                return FileError("A imagem deve ter no maximo 2 MB.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var data = ms.ToArray();
            // o tipo vem do conteudo (assinatura do arquivo), nunca do nome/Content-Type enviado
            var contentType = ImageSniffer.Detect(data);
            if (contentType is null)
                return FileError("Formato nao suportado. Use JPEG, PNG ou WebP.");

            var image = await _context.ProductImages.FirstOrDefaultAsync(i => i.ProductId == id, ct);
            if (image is null) _context.ProductImages.Add(new ProductImage { ProductId = id, ContentType = contentType, Data = data });
            else { image.ContentType = contentType; image.Data = data; }
            product.ImageUpdatedAt = DateTime.UtcNow;
            product.UpdatedAt = product.ImageUpdatedAt.Value;
            await _context.SaveChangesAsync(ct);
            await _audit.LogAsync("produto_imagem", "ok", Actor, null, Label(product));
            return Ok(product);
        }

        /// <summary>400 no mesmo formato da validacao automatica (ProblemDetails com code "validation_error").</summary>
        private ActionResult FileError(string message)
        {
            ModelState.AddModelError("file", message);
            var pd = ProblemDetailsFactory.CreateValidationProblemDetails(HttpContext, ModelState);
            pd.Extensions["code"] = "validation_error";
            return new BadRequestObjectResult(pd) { ContentTypes = { "application/problem+json" } };
        }

        // DELETE: api/products/5/image
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpDelete("{id}/image")]
        public async Task<IActionResult> DeleteImage(int id, CancellationToken ct)
        {
            var product = await _context.Products.FindAsync(new object[] { id }, ct);
            if (product == null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found", detail: $"Product with ID {id} not found.");

            var image = await _context.ProductImages.FirstOrDefaultAsync(i => i.ProductId == id, ct);
            if (image is not null) _context.ProductImages.Remove(image);
            product.ImageUpdatedAt = null;
            product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            if (image is not null) await _audit.LogAsync("produto_imagem_removida", "ok", Actor, null, Label(product));
            return NoContent();
        }
    }
}
