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
    /// <summary>
    /// Vendas do balcao (Admin e Operador): varios itens + forma de pagamento, gravados de uma vez.
    /// Desfazer: ate <see cref="Sale.UndoWindow"/> depois, por quem registrou (ou um Admin).
    /// </summary>
    [Authorize(Policy = AuthSetup.SalesPolicy)]
    [Route("api/sales")]
    public class SalesController : ApiControllerBase
    {
        public const int MaxItems = 100;
        public const int MaxListSize = 100;

        private readonly AppDbContext _db;
        private readonly StockService _stock;

        public SalesController(AppDbContext db, StockService stock)
        {
            _db = db;
            _stock = stock;
        }

        private bool IsAdmin => User.IsInRole(Roles.Admin);

        // POST: api/sales  {"paymentMethod":"Pix","items":[{"variantId":12,"quantity":2},{"variantId":15,"quantity":4}]}
        [HttpPost]
        public async Task<IActionResult> Create(CreateSaleDto dto, CancellationToken ct)
        {
            var errors = new Dictionary<string, string>();
            var payment = PaymentMethods.Parse(dto.PaymentMethod);
            if (payment is null) errors["paymentMethod"] = "Escolha a forma de pagamento: Dinheiro, Pix, Debito ou Credito.";
            if (dto.Items.Count == 0) errors["items"] = "Adicione pelo menos um item.";
            else if (dto.Items.Count > MaxItems) errors["items"] = $"No maximo {MaxItems} itens por venda.";
            else if (dto.Items.GroupBy(i => i.VariantId).Any(g => g.Sum(i => (long)i.Quantity) > Limits.MaxValue))
                errors["items"] = "Quantidade invalida.";
            if (errors.Count > 0) return Fail(400, "validation_error", "Dados invalidos", errors: errors);

            var result = await _stock.SellAsync(payment!, dto.Items.Select(i => (i.VariantId, i.Quantity)), ActorName, ct);
            return result.Outcome switch
            {
                SaleOutcome.VariantNotFound => Fail(409, "variant_not_found", "Item indisponivel",
                    "Um dos itens foi removido do cadastro. Atualize a tela e monte a venda de novo."),
                SaleOutcome.InsufficientStock => InsufficientStock(result.Shortages!),
                _ => StatusCode(201, await LoadAsync(result.Sale!.Id, ct))
            };
        }

        private ObjectResult InsufficientStock(IReadOnlyList<Shortage> shortages)
        {
            var first = shortages[0];
            var detail = shortages.Count == 1
                ? $"{first.ProductName} ({first.VariantLabel}): pedido {first.Requested}, em estoque {first.Available}."
                : $"{shortages.Count} itens sem estoque suficiente.";
            var problem = Fail(409, "insufficient_stock", "Estoque insuficiente", detail);
            ((ProblemDetails)problem.Value!).Extensions["shortages"] = shortages;
            return problem;
        }

        // POST: api/sales/7/undo
        [HttpPost("{id:long}/undo")]
        public async Task<IActionResult> Undo(long id, CancellationToken ct)
        {
            var result = await _stock.UndoSaleAsync(id, ActorName, IsAdmin, ct);
            return result.Outcome switch
            {
                SaleOutcome.NotFound or SaleOutcome.NotOwner => Fail(404, "not_found", "Venda nao encontrada", $"Venda {id} nao encontrada."),
                SaleOutcome.AlreadyUndone => Fail(409, "already_undone", "Venda ja desfeita", "Esta venda ja foi desfeita."),
                SaleOutcome.UndoExpired => Fail(409, "undo_expired", "Prazo encerrado",
                    $"So da para desfazer ate {(int)Sale.UndoWindow.TotalMinutes} minutos depois da venda. Peca ao administrador para corrigir o estoque."),
                SaleOutcome.OutOfRange => Fail(409, "quantity_out_of_range", "Quantidade invalida",
                    $"O saldo deve ficar entre 0 e {Limits.MaxValue}."),
                _ => Ok(await LoadAsync(id, ct))
            };
        }

        // GET: api/sales?limit=20  (mais recentes primeiro; o Operador ve so as proprias)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int limit = 20, CancellationToken ct = default)
        {
            var query = _db.Sales.AsNoTracking().AsQueryable();
            if (!IsAdmin) { var me = ActorName; query = query.Where(s => s.CreatedBy == me); }
            var sales = await query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
                .Take(Math.Clamp(limit, 1, MaxListSize)).ToListAsync(ct);
            var ids = sales.Select(s => s.Id).ToArray();
            var outs = (await _db.StockMovements.AsNoTracking()
                .Where(m => m.SaleId != null && ids.Contains(m.SaleId.Value) && m.Type == MovementTypes.Out)
                .ToListAsync(ct)).ToLookup(m => m.SaleId!.Value);
            var now = DateTime.UtcNow;
            return Ok(sales.Select(s => SaleDto.From(s, outs[s.Id], now)));
        }

        // GET: api/sales/7
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct)
        {
            var sale = await _db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            if (sale is null || (!IsAdmin && !string.Equals(sale.CreatedBy, ActorName, StringComparison.OrdinalIgnoreCase)))
                return Fail(404, "not_found", "Venda nao encontrada", $"Venda {id} nao encontrada.");
            return Ok(await LoadAsync(id, ct));
        }

        private async Task<SaleDto> LoadAsync(long id, CancellationToken ct)
        {
            var sale = await _db.Sales.AsNoTracking().FirstAsync(s => s.Id == id, ct);
            var outs = await _db.StockMovements.AsNoTracking()
                .Where(m => m.SaleId == id && m.Type == MovementTypes.Out).ToListAsync(ct);
            return SaleDto.From(sale, outs, DateTime.UtcNow);
        }
    }
}
