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

        // GET: api/products
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts([FromQuery, MaxLength(Limits.MaxQueryLength)] string? search, [FromQuery, MaxLength(Limits.MaxQueryLength)] string? category)
        {
            var query = _context.Products.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{EscapeLike(search.Trim())}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, pattern, "\\") ||
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

        // GET: api/products/stats
        [HttpGet("stats")]
        public async Task<ActionResult<object>> GetStats()
        {
            var products = await _context.Products.ToListAsync();
            var totalProducts = products.Count;
            var totalItems = products.Sum(p => (long)p.Quantity);
            var totalValue = products.Sum(p => p.Quantity * p.Price);
            var totalCost = products.Sum(p => p.Quantity * p.Cost);
            var lowStockCount = products.Count(p => p.Quantity > 0 && p.Quantity <= p.MinStock);
            var outOfStockCount = products.Count(p => p.Quantity == 0);
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
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found", detail: $"Product with ID {id} not found.");
            }

            return product;
        }

        // POST: api/products
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPost]
        public async Task<ActionResult<Product>> CreateProduct(CreateProductDto dto)
        {
            var product = new Product
            {
                Name = dto.Name,
                Description = dto.Description,
                Category = dto.Category,
                Quantity = dto.Quantity,
                Price = dto.Price,
                Cost = dto.Cost,
                MinStock = dto.MinStock,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Products.Add(product);
            // saldo inicial entra no historico, para o saldo sempre bater com a soma das movimentacoes
            if (product.Quantity > 0)
                _context.StockMovements.Add(StockService.NewMovement(product, MovementTypes.In, product.Quantity,
                    "Estoque inicial", product.CreatedAt, Actor));
            await _context.SaveChangesAsync();
            await _audit.LogAsync("produto_criado", "ok", Actor, null, Label(product));

            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }

        // PUT: api/products/5
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, UpdateProductDto dto)
        {
            var product = await _context.Products.FindAsync(id);

            if (product == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found", detail: $"Product with ID {id} not found.");
            }

            var delta = dto.Quantity - product.Quantity;
            product.Name = dto.Name;
            product.Description = dto.Description;
            product.Category = dto.Category;
            product.Quantity = dto.Quantity;
            product.Price = dto.Price;
            product.Cost = dto.Cost;
            product.MinStock = dto.MinStock;
            product.UpdatedAt = DateTime.UtcNow;

            // mudanca de saldo pela edicao fica registrada como ajuste (o caminho normal e /movements)
            if (delta != 0)
                _context.StockMovements.Add(StockService.NewMovement(product, MovementTypes.Adjust, delta,
                    "Edicao do produto", product.UpdatedAt, Actor));
            await _context.SaveChangesAsync();
            await _audit.LogAsync("produto_editado", "ok", Actor, null, Label(product));

            return Ok(product);
        }

        // PATCH: api/products/5/quantity  {"delta": -1}
        // Ajuste rapido (botoes +/-): registrado como movimentacao do tipo Ajuste. Atomico (linha travada na transacao).
        [Authorize(Policy = AuthSetup.StockPolicy)]
        [HttpPatch("{id}/quantity")]
        public async Task<ActionResult<Product>> AdjustQuantity(int id, AdjustQuantityDto dto, CancellationToken ct)
        {
            var result = await _stock.ApplyDeltaAsync(id, MovementTypes.Adjust, dto.Delta, "Ajuste rapido",
                DateTime.UtcNow, Actor, ct);

            return result.Outcome switch
            {
                StockOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found",
                    detail: $"Product with ID {id} not found."),
                StockOutcome.OutOfRange => Problem(statusCode: StatusCodes.Status409Conflict, title: "Invalid stock quantity",
                    detail: $"Quantity must stay between 0 and {Limits.MaxValue}."),
                _ => Ok(result.Product)
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
