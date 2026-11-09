using backend.Data;
using backend.DTOs;
using backend.Models;
using System.ComponentModel.DataAnnotations;
using backend.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

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
            var lowStockCount = products.Count(p => p.Quantity > 0 && p.Quantity <= p.MinStock);
            var outOfStockCount = products.Count(p => p.Quantity == 0);
            var categories = products.Select(p => p.Category).Distinct().Count();

            return Ok(new
            {
                totalProducts,
                totalItems,
                totalValue,
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
                MinStock = dto.MinStock,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

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

            product.Name = dto.Name;
            product.Description = dto.Description;
            product.Category = dto.Category;
            product.Quantity = dto.Quantity;
            product.Price = dto.Price;
            product.MinStock = dto.MinStock;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(product);
        }

        // PATCH: api/products/5/quantity  {"delta": -1}
        // Ajuste atomico no banco (Quantity = Quantity + delta), sem ler-modificar-gravar: seguro com cliques concorrentes.
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPatch("{id}/quantity")]
        public async Task<ActionResult<Product>> AdjustQuantity(int id, AdjustQuantityDto dto, CancellationToken ct)
        {
            var delta = dto.Delta;
            var now = DateTime.UtcNow;

            var affected = await _context.Products
                .Where(p => p.Id == id && p.Quantity + delta >= 0 && p.Quantity + delta <= Limits.MaxValue)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Quantity, p => p.Quantity + delta)
                    .SetProperty(p => p.UpdatedAt, now), ct);

            if (affected == 0)
            {
                var exists = await _context.Products.AnyAsync(p => p.Id == id, ct);
                if (!exists)
                {
                    return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found",
                        detail: $"Product with ID {id} not found.");
                }

                return Problem(statusCode: StatusCodes.Status409Conflict, title: "Invalid stock quantity",
                    detail: $"Quantity must stay between 0 and {Limits.MaxValue}.");
            }

            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (product == null)
            {
                // removido entre o UPDATE e a leitura
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Product not found",
                    detail: $"Product with ID {id} not found.");
            }
            return Ok(product);
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

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
