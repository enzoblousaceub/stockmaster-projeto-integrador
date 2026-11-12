using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using backend.Models;

namespace backend.Security
{
    public interface IEmailSender
    {
        /// <summary>Envia o e-mail. Devolve false se nao enviou (nao configurado ou erro); nunca lanca.</summary>
        Task<bool> SendAsync(string to, string subject, string html, string text, CancellationToken ct);
    }

    /// <summary>Envio via API HTTP do Resend (https://resend.com/docs/api-reference/emails/send-email).</summary>
    public class ResendEmailSender : IEmailSender
    {
        private readonly HttpClient _http;
        private readonly string? _apiKey;
        private readonly string? _from;
        private readonly ILogger<ResendEmailSender> _log;

        public ResendEmailSender(HttpClient http, IConfiguration config, ILogger<ResendEmailSender> log)
        {
            _http = http;
            _apiKey = config["Email:ResendApiKey"];
            _from = config["Email:From"];
            _log = log;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_from);

        public async Task<bool> SendAsync(string to, string subject, string html, string text, CancellationToken ct)
        {
            if (!IsConfigured)
            {
                _log.LogWarning("E-mail nao configurado (Email__ResendApiKey / Email__From): convite nao enviado.");
                return false;
            }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                req.Content = JsonContent.Create(new { from = _from, to = new[] { to }, subject, html, text });
                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) return true;
                _log.LogError("Resend recusou o envio (HTTP {Status}).", (int)resp.StatusCode);
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _log.LogError(ex, "Falha ao chamar o Resend.");
                return false;
            }
        }
    }

    /// <summary>Monta e envia o convite de ativacao. O codigo vai no fragmento (#) do link: nao chega a logs nem ao Referer.</summary>
    public class InviteMailer
    {
        private readonly IEmailSender _sender;
        private readonly IConfiguration _config;

        public InviteMailer(IEmailSender sender, IConfiguration config) { _sender = sender; _config = config; }

        public async Task<bool> SendAsync(AppUser user, string code, HttpRequest request, CancellationToken ct)
        {
            var baseUrl = _config["App:PublicBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = $"{request.Scheme}://{request.Host}";
            var link = $"{baseUrl.TrimEnd('/')}/ativar#email={Uri.EscapeDataString(user.Email)}&codigo={Uri.EscapeDataString(code)}";
            var hours = (int)ActivationCodes.Lifetime.TotalHours;

            var text = $"Segue convite para criar conta no Stock Master.\n\n" +
                       $"Abra o link para definir a sua senha (vale por {hours} horas e so pode ser usado uma vez):\n{link}\n\n" +
                       $"E-mail: {user.Email}\nCodigo de acesso: {code}\n\nSe voce nao esperava este e-mail, ignore-o.";
            var html = $"""
                <div style="font-family:Arial,sans-serif;max-width:480px;margin:auto;color:#1f2937">
                  <h2>Segue convite para criar conta</h2>
                  <p>Voc&ecirc; foi convidado(a) para o Stock Master. Clique para definir a sua senha:</p>
                  <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;padding:12px 20px;background:#2563eb;color:#fff;border-radius:6px;text-decoration:none;font-weight:bold">Criar minha conta</a></p>
                  <p>E-mail: <b>{WebUtility.HtmlEncode(user.Email)}</b><br>C&oacute;digo de acesso: <code style="font-size:18px;font-weight:bold">{WebUtility.HtmlEncode(code)}</code></p>
                  <p style="color:#6b7280;font-size:13px">O c&oacute;digo vale por {hours} horas e s&oacute; pode ser usado uma vez. Se voc&ecirc; n&atilde;o esperava este e-mail, ignore-o.</p>
                </div>
                """;
            return await _sender.SendAsync(user.Email, "Convite para criar conta no Stock Master", html, text, ct);
        }
    }
}
