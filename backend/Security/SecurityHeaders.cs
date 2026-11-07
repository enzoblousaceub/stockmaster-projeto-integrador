using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace backend.Security
{
    /// <summary>Cabecalhos de seguranca e CSP. O hash dos scripts inline do index.html e calculado na inicializacao.</summary>
    public static class SecurityHeaders
    {
        private static readonly Regex InlineScript = new(
            @"<script(?<attrs>[^>]*)>(?<body>.*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Retorna 'sha256-...' para cada script inline (sem src) do HTML.</summary>
        public static IReadOnlyList<string> InlineScriptHashes(string html)
        {
            var hashes = new List<string>();
            foreach (Match m in InlineScript.Matches(html))
            {
                if (Regex.IsMatch(m.Groups["attrs"].Value, @"\bsrc\s*=", RegexOptions.IgnoreCase)) continue;
                var body = m.Groups["body"].Value;
                if (string.IsNullOrWhiteSpace(body)) continue;
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(body));
                hashes.Add($"'sha256-{Convert.ToBase64String(hash)}'");
            }
            return hashes;
        }

        public static string BuildCsp(IEnumerable<string> scriptHashes)
        {
            var scriptSrc = string.Join(' ', new[] { "'self'" }.Concat(scriptHashes));
            return string.Join("; ",
                "default-src 'self'",
                $"script-src {scriptSrc}",
                // Angular injeta <style> dos componentes em runtime: 'unsafe-inline' so para estilos
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
                "font-src 'self' https://fonts.gstatic.com",
                "img-src 'self' data:",
                "connect-src 'self'",
                "manifest-src 'self'",
                "worker-src 'self'",
                "object-src 'none'",
                "base-uri 'self'",
                "form-action 'self'",
                "frame-ancestors 'none'");
        }

        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, string csp, bool hsts)
        {
            return app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    var h = context.Response.Headers;
                    h["Content-Security-Policy"] = csp;
                    h["X-Content-Type-Options"] = "nosniff";
                    h["X-Frame-Options"] = "DENY";
                    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                    h["Cross-Origin-Opener-Policy"] = "same-origin";
                    if (hsts && context.Request.IsHttps)
                        h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                    if (context.Request.Path.StartsWithSegments("/api"))
                        h["Cache-Control"] = "no-store";
                    return Task.CompletedTask;
                });
                await next();
            });
        }
    }
}
