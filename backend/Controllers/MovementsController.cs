using backend.Data;
using backend.DTOs;
using backend.Models;
using backend.Security;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    /// <summary>Movimentacoes de estoque (RF006 entrada, RF007 saida, RF008 saldo automatico). Vendas: ver SalesController.</summary>
    [Route("api")]
    public class MovementsController : ApiControllerBase
    {
        public const int MaxListSize = 500;

        private readonly AppDbContext _db;
        private readonly StockService _stock;

        public MovementsController(AppDbContext db, StockService stock)
        {
            _db = db;
            _stock = stock;
        }

        // POST: api/products/5/movements  {"type":"Entrada","variantId":12,"quantity":2,"reason":"Compra","occurredAt":"..."}
        [Authorize(Policy = AuthSetup.AdminPolicy)]
        [HttpPost("products/{id:int}/movements")]
        public async Task<IActionResult> Create(int id, CreateMovementDto dto, CancellationToken ct)
        {
            var errors = new Dictionary<string, string>();
            var type = StockService.ParseType(dto.Type);
            if (type is null) errors["type"] = "Tipo deve ser Entrada, Saida ou Ajuste.";
            if (type is MovementTypes.In or MovementTypes.Out && dto.Quantity < 1)
                errors["quantity"] = "Informe uma quantidade maior que zero.";
            var reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (type == MovementTypes.Out && reason is null) errors["reason"] = "Informe o motivo da saida.";

            var now = DateTime.UtcNow;
            var occurredAt = dto.OccurredAt?.UtcDateTime ?? now;
            // tolerancia de 5 min para relogio do aparelho adiantado
            if (occurredAt > now.AddMinutes(5)) errors["occurredAt"] = "A data nao pode estar no futuro.";
            else if (occurredAt < now.AddYears(-5)) errors["occurredAt"] = "Data muito antiga.";

            if (errors.Count > 0) return Fail(400, "validation_error", "Dados invalidos", errors: errors);

            var result = type == MovementTypes.Adjust
                ? await _stock.SetCountedAsync(id, dto.VariantId, dto.Quantity, reason ?? "Conferencia de estoque", occurredAt, ActorName, ct)
                : await _stock.ApplyDeltaAsync(id, dto.VariantId, type!, type == MovementTypes.In ? dto.Quantity : -dto.Quantity,
                    reason, occurredAt, ActorName, ct);

            if (result.Outcome == StockOutcome.Ok)
            {
                var product = await _db.Products.AsNoTracking().Include(p => p.Variants).FirstAsync(p => p.Id == id, ct);
                product.Variants = product.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id).ToList();
                return StatusCode(201, new MovementResultDto(MovementDto.From(result.Movement!), product));
            }
            return StockFailure(result, id, type);
        }

        /// <summary>Respostas de erro comuns a movimentacao e ajuste rapido.</summary>
        internal ObjectResult StockFailure(StockResult result, int id, string? type) => result.Outcome switch
        {
            StockOutcome.NotFound => Fail(404, "not_found", "Produto nao encontrado", $"Produto {id} nao encontrado."),
            StockOutcome.VariantRequired => Fail(400, "validation_error", "Dados invalidos",
                errors: new Dictionary<string, string> { ["variantId"] = "Escolha o tamanho/material." }),
            StockOutcome.VariantNotFound => Fail(400, "validation_error", "Dados invalidos",
                errors: new Dictionary<string, string> { ["variantId"] = "Variacao nao pertence a este produto." }),
            StockOutcome.OutOfRange when type == MovementTypes.Out => Fail(409, "insufficient_stock", "Estoque insuficiente",
                $"Saldo atual: {result.Variant!.Quantity}. A saida nao pode deixar o estoque negativo."),
            StockOutcome.OutOfRange => Fail(409, "quantity_out_of_range", "Quantidade invalida",
                $"O saldo deve ficar entre 0 e {Limits.MaxValue}."),
            _ => Fail(409, "no_change", "Nada a ajustar", $"O saldo ja e {result.Variant!.Quantity}.")
        };

        // GET: api/products/5/movements?limit=100  (historico do produto, mais recentes primeiro)
        [Authorize(Policy = AuthSetup.ReportsPolicy)]
        [HttpGet("products/{id:int}/movements")]
        public async Task<IActionResult> ListForProduct(int id, [FromQuery] int limit = 100, CancellationToken ct = default)
        {
            if (!await _db.Products.AnyAsync(p => p.Id == id, ct))
                return Fail(404, "not_found", "Produto nao encontrado", $"Produto {id} nao encontrado.");

            var rows = await _db.StockMovements.AsNoTracking()
                .Where(m => m.ProductId == id)
                .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Id)
                .Take(Math.Clamp(limit, 1, MaxListSize))
                .ToListAsync(ct);
            return Ok(rows.Select(MovementDto.From));
        }

        // GET: api/movements?type=Saida&from=...&to=...&limit=100  (todas as movimentacoes, mais recentes primeiro)
        [Authorize(Policy = AuthSetup.ReportsPolicy)]
        [HttpGet("movements")]
        public async Task<IActionResult> List([FromQuery] string? type, [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to, [FromQuery] int limit = 100, CancellationToken ct = default)
        {
            var query = _db.StockMovements.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(type))
            {
                var parsed = StockService.ParseType(type);
                if (parsed is null)
                    return Fail(400, "validation_error", "Dados invalidos",
                        errors: new Dictionary<string, string> { ["type"] = "Tipo deve ser Entrada, Saida ou Ajuste." });
                query = query.Where(m => m.Type == parsed);
            }
            if (from is not null) { var f = from.Value.UtcDateTime; query = query.Where(m => m.OccurredAt >= f); }
            if (to is not null) { var t = to.Value.UtcDateTime; query = query.Where(m => m.OccurredAt <= t); }

            var rows = await query
                .OrderByDescending(m => m.OccurredAt).ThenByDescending(m => m.Id)
                .Take(Math.Clamp(limit, 1, MaxListSize))
                .ToListAsync(ct);
            return Ok(rows.Select(MovementDto.From));
        }
    }
}
