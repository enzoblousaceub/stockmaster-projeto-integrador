using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers
{
    /// <summary>Relatorios de movimentacao e previsao de ruptura/reposicao (qualquer perfil logado).</summary>
    [Route("api/reports")]
    public class ReportsController : ApiControllerBase
    {
        private readonly ForecastService _forecast;

        public ReportsController(ForecastService forecast) => _forecast = forecast;

        // GET: api/reports/summary?days=30
        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] int days = 30, CancellationToken ct = default)
        {
            if (days < 7 || days > 365)
                return Fail(400, "validation_error", "Dados invalidos",
                    errors: new Dictionary<string, string> { ["days"] = "O periodo deve ter entre 7 e 365 dias." });
            return Ok(await _forecast.SummaryAsync(days, ct));
        }
    }
}
