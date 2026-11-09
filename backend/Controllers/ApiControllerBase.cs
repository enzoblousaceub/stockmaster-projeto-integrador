using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace backend.Controllers
{
    [ApiController]
    public abstract class ApiControllerBase : ControllerBase
    {
        /// <summary>Erro em ProblemDetails com "code" estavel, traceId e (opcional) errors/Retry-After.</summary>
        protected ObjectResult Fail(int status, string code, string title, string? detail = null,
            IDictionary<string, string>? errors = null, int? retryAfterSeconds = null)
        {
            ProblemDetails pd;
            if (errors is { Count: > 0 })
            {
                var ms = new ModelStateDictionary();
                foreach (var (k, v) in errors) ms.AddModelError(k, v);
                pd = ProblemDetailsFactory.CreateValidationProblemDetails(HttpContext, ms, status, title, detail: detail);
            }
            else
            {
                pd = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title, detail: detail);
            }
            pd.Extensions["code"] = code;
            if (retryAfterSeconds is int s)
            {
                pd.Extensions["retryAfterSeconds"] = s;
                Response.Headers.RetryAfter = s.ToString();
            }
            return new ObjectResult(pd) { StatusCode = status, ContentTypes = { "application/problem+json" } };
        }

        protected ObjectResult Locked(int seconds) => Fail(429, "account_locked", "Muitas tentativas",
            $"Tente novamente em {seconds} segundos.", retryAfterSeconds: seconds);

        protected ObjectResult RateLimited(int seconds) => Fail(429, "rate_limited", "Muitas tentativas",
            $"Tente novamente em {seconds} segundos.", retryAfterSeconds: seconds);

        protected string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        protected string? ActorName => User.Identity?.Name;
    }
}
