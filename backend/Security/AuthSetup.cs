using System.Security.Claims;
using backend.Data;
using backend.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace backend.Security
{
    /// <summary>Erros da API sempre em ProblemDetails com "code" estavel (e traceId).</summary>
    public static class ProblemWriter
    {
        public static async Task WriteAsync(HttpContext ctx, int status, string code, string title, string? detail = null,
            int? retryAfterSeconds = null)
        {
            if (ctx.Response.HasStarted) return;
            ctx.Response.StatusCode = status;
            if (retryAfterSeconds is int s) ctx.Response.Headers.RetryAfter = s.ToString();
            var pd = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Title = title, Detail = detail };
            pd.Extensions["code"] = code;
            if (retryAfterSeconds is int r) pd.Extensions["retryAfterSeconds"] = r;
            await ctx.RequestServices.GetRequiredService<IProblemDetailsService>()
                .WriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd });
        }
    }

    public static class AuthSetup
    {
        public const string AdminPolicy = "AdminOnly";
        public const string XsrfCookie = "XSRF-TOKEN";
        public const string XsrfHeader = "X-XSRF-TOKEN";
        private const string StampClaim = "sm:stamp";
        private const string AbsoluteClaim = "sm:abs";
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(8);
        public static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(24);

        public static bool SecureCookies(IConfiguration config) => config.GetValue("Auth:SecureCookies", true);

        public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration config)
        {
            var secure = SecureCookies(config);
            var policy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

            // chaves de sessao/antiforgery no Postgres: sobrevivem a deploys e reinicios (o disco do Render e efemero)
            services.AddDataProtection()
                .SetApplicationName("stockmaster")
                .PersistKeysToDbContext<AppDbContext>();

            services.AddAntiforgery(o =>
            {
                o.HeaderName = XsrfHeader;
                o.Cookie.Name = secure ? "__Host-sm.af" : "sm.af";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = policy;
                o.Cookie.Path = "/";
            });

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o =>
                {
                    o.Cookie.Name = secure ? "__Host-sm.auth" : "sm.auth";
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SameSite = SameSiteMode.Strict;
                    o.Cookie.SecurePolicy = policy;
                    o.Cookie.Path = "/";
                    o.ExpireTimeSpan = IdleTimeout;
                    o.SlidingExpiration = true;
                    o.Events = new CookieAuthenticationEvents
                    {
                        OnRedirectToLogin = ctx => ProblemWriter.WriteAsync(ctx.HttpContext, 401, "unauthenticated", "Nao autenticado"),
                        OnRedirectToAccessDenied = ctx => ProblemWriter.WriteAsync(ctx.HttpContext, 403, "forbidden", "Sem permissao",
                            "Seu perfil nao permite esta acao."),
                        OnValidatePrincipal = ValidateSessionAsync
                    };
                });

            services.AddAuthorization(o =>
            {
                // deny-by-default: tudo exige sessao, exceto o que tem [AllowAnonymous] explicito
                o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                o.AddPolicy(AdminPolicy, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin));
            });

            // chaves do Data Protection cifradas com Auth__KeyEncryptionKey (quem le o banco nao forja cookies)
            var kek = EnvKeyXmlEncryptor.ParseKey(config["Auth:KeyEncryptionKey"]);
            if (kek is not null)
                services.Configure<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>(o => o.XmlEncryptor = new EnvKeyXmlEncryptor(kek));

            services.AddHttpContextAccessor();
            services.AddExceptionHandler<HasherBusyHandler>();
            services.AddSingleton<BlockedAuditGate>();
            services.AddSingleton<Pbkdf2PasswordHasher>();
            services.AddSingleton<ActivationCodes>();
            services.AddHttpClient<IEmailSender, ResendEmailSender>(c => c.Timeout = TimeSpan.FromSeconds(10));
            services.AddScoped<InviteMailer>();
            services.AddSingleton<IpFailureLimiter>();
            services.AddScoped<AuthThrottleService>();
            services.AddScoped<AuditService>();
            services.AddHostedService<RetentionService>();
            return services;
        }

        /// <summary>Valida a sessao no banco a cada request: usuario ativo, mesmo SecurityStamp e dentro do teto de 24h.</summary>
        private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
        {
            // arquivos estaticos/healthz nao precisam de sessao: evita uma consulta ao banco por asset
            if (!ctx.HttpContext.Request.Path.StartsWithSegments("/api")) return;

            var principal = ctx.Principal;
            var idText = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = principal?.FindFirstValue(StampClaim);
            var abs = principal?.FindFirstValue(AbsoluteClaim);
            if (!int.TryParse(idText, out var id) || stamp is null || !long.TryParse(abs, out var absUnix)
                || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > absUnix)
            {
                await Reject(ctx);
                return;
            }

            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().Where(u => u.Id == id)
                .Select(u => new { u.IsActive, u.SecurityStamp, u.PasswordHash, u.Role }).FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
            if (user is null || !user.IsActive || user.PasswordHash is null || user.SecurityStamp != stamp)
                await Reject(ctx);
        }

        private static async Task Reject(CookieValidatePrincipalContext ctx)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        public static ClaimsPrincipal BuildPrincipal(AppUser user, long? absoluteUnix = null)
        {
            var abs = absoluteUnix ?? DateTimeOffset.UtcNow.Add(AbsoluteTimeout).ToUnixTimeSeconds();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(StampClaim, user.SecurityStamp),
                new Claim(AbsoluteClaim, abs.ToString())
            }, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        public static long? AbsoluteOf(ClaimsPrincipal p) =>
            long.TryParse(p.FindFirstValue(AbsoluteClaim), out var v) ? v : null;

        /// <summary>Emite o cookie XSRF-TOKEN (legivel por JS) amarrado ao usuario atual. Chamar com HttpContext.User ja definido.</summary>
        public static void IssueCsrfToken(HttpContext ctx)
        {
            var tokens = ctx.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(ctx);
            ctx.Response.Cookies.Append(XsrfCookie, tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
                Secure = SecureCookies(ctx.RequestServices.GetRequiredService<IConfiguration>()),
                Path = "/"
            });
        }

        public static void ClearCsrfCookies(HttpContext ctx)
        {
            ctx.Response.Cookies.Delete(XsrfCookie, new CookieOptions { Path = "/" });
        }

        /// <summary>
        /// CSRF em duas camadas para TODO POST/PUT/PATCH/DELETE em /api: (a) Origin/Sec-Fetch-Site; (b) token do antiforgery
        /// (header X-XSRF-TOKEN) para requests autenticados, exceto login/ativar. Falha = 403 csrf_invalid.
        /// </summary>
        /// <summary>
        /// Cookies Secure exigem HTTPS. Atras do proxy o esquema vem do X-Forwarded-Proto; se faltar, falha com mensagem
        /// clara (400 https_required + log Error) em vez de um 500 obscuro.
        /// </summary>
        public static IApplicationBuilder UseHttpsRequiredForSecureCookies(this IApplicationBuilder app, IConfiguration config)
        {
            var secure = SecureCookies(config);
            var lastLog = DateTime.MinValue;
            return app.Use(async (ctx, next) =>
            {
                if (secure && !ctx.Request.IsHttps && ctx.Request.Path.StartsWithSegments("/api"))
                {
                    if (DateTime.UtcNow - lastLog > TimeSpan.FromMinutes(1))
                    {
                        lastLog = DateTime.UtcNow;
                        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth").LogError(
                            "Auth:SecureCookies=true mas a requisicao chegou por HTTP. Em producao o proxy deve enviar X-Forwarded-Proto: https; " +
                            "para rodar em http://localhost defina Auth__SecureCookies=false.");
                    }
                    await ProblemWriter.WriteAsync(ctx, 400, "https_required", "HTTPS necessario",
                        "Esta instalacao exige HTTPS para a autenticacao.");
                    return;
                }
                await next();
            });
        }

        public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
        {
            var m = ctx.Request.Method;
            var unsafeMethod = HttpMethods.IsPost(m) || HttpMethods.IsPut(m) || HttpMethods.IsPatch(m) || HttpMethods.IsDelete(m);
            if (!unsafeMethod || !ctx.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            if (!SameOrigin(ctx.Request))
            {
                await ProblemWriter.WriteAsync(ctx, 403, "csrf_invalid", "Requisicao bloqueada", "Origem da requisicao nao permitida.");
                return;
            }

            var anonymousEntry = ctx.Request.Path.StartsWithSegments("/api/auth/login") || ctx.Request.Path.StartsWithSegments("/api/auth/ativar");
            if (ctx.User.Identity?.IsAuthenticated == true && !anonymousEntry)
            {
                try
                {
                    await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
                }
                catch (AntiforgeryValidationException)
                {
                    await ProblemWriter.WriteAsync(ctx, 403, "csrf_invalid", "Requisicao bloqueada", "Token CSRF ausente ou invalido.");
                    return;
                }
            }
            await next();
        });

        private static bool SameOrigin(HttpRequest req)
        {
            if (req.Headers.TryGetValue("Sec-Fetch-Site", out var site))
            {
                var v = site.ToString();
                if (v != "same-origin" && v != "none") return false;
            }
            if (req.Headers.TryGetValue("Origin", out var origin))
            {
                var expected = $"{req.Scheme}://{req.Host}";
                if (!string.Equals(origin.ToString(), expected, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }
}
