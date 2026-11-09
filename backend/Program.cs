using backend.Data;
using backend.Security;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Erros padronizados em RFC 7807 (application/problem+json), sempre com traceId
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
    };
});

// Erros de validacao: ProblemDetails com code "validation_error" (chaves em camelCase na area de auth)
builder.Services.Configure<ApiBehaviorOptions>(o =>
{
    o.InvalidModelStateResponseFactory = ctx =>
    {
        var factory = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory>();
        var ms = ctx.ModelState;
        if (ctx.HttpContext.Request.Path.StartsWithSegments("/api/auth") || ctx.HttpContext.Request.Path.StartsWithSegments("/api/users"))
        {
            var camel = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            foreach (var (key, entry) in ms)
            {
                var k = key.Length > 0 && char.IsUpper(key[0]) ? char.ToLowerInvariant(key[0]) + key[1..] : key;
                foreach (var e in entry.Errors) camel.AddModelError(k, e.ErrorMessage);
            }
            ms = camel;
        }
        var pd = factory.CreateValidationProblemDetails(ctx.HttpContext, ms);
        pd.Extensions["code"] = "validation_error";
        return new BadRequestObjectResult(pd) { ContentTypes = { "application/problem+json" } };
    };
});

// Autenticacao por cookie (HttpOnly, SameSite=Strict), antiforgery, deny-by-default e Data Protection no Postgres
builder.Services.AddAppAuth(builder.Configuration);

// PostgreSQL (DATABASE_URL no Render; ConnectionStrings:Default em dev/testes)
var connectionString = DatabaseConnection.Resolve(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// CORS: o front e servido pelo mesmo host, entao em producao nao ha CORS.
// So habilita para as origens listadas em Cors:AllowedOrigins (ex.: http://localhost:4200 em dev).
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("Configured", policy => policy
        .WithOrigins(corsOrigins)
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .WithHeaders("Content-Type")));
}

// Atras do proxy do Render (TLS terminado na borda): confia no X-Forwarded-* de um unico salto
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate limit por IP, so na API (/api). Leitura e escrita tem limites separados (por minuto, configuraveis).
var readLimit = builder.Configuration.GetValue("RateLimit:ReadPerMinute", 300);
var writeLimit = builder.Configuration.GetValue("RateLimit:WritePerMinute", 120);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
            return RateLimitPartition.GetNoLimiter("static");

        var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter($"{(isRead ? "r" : "w")}:{ip}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = isRead ? readLimit : writeLimit,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0
        });
    });
    options.OnRejected = async (ctx, ct) =>
    {
        // a janela deslizante nem sempre informa o tempo exato; usa o tamanho de um segmento (10s) como minimo
        var seconds = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 10;
        ctx.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
        var problems = ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx.HttpContext,
            ProblemDetails = { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests", Detail = "Rate limit exceeded. Try again shortly." }
        });
    };
});

// Configure Kestrel (respects PORT/ASPNETCORE_HTTP_PORTS environment variables or defaults to 8080)
var portEnv = Environment.GetEnvironmentVariable("PORT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
var listenPort = (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out var p)) ? p : 8080;
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(listenPort);
});

var app = builder.Build();

// Configure pipeline
// Excecoes nao tratadas -> 500 ProblemDetails generico (sem stack/mensagem interna); detalhes vao so para o log
app.UseExceptionHandler();
// Respostas 4xx/5xx vazias (ex.: 405, 415) tambem viram ProblemDetails
app.UseStatusCodePages();
app.UseForwardedHeaders();
// HTTPS e redirecionamento ficam na borda do Render; aqui so HSTS (quando a requisicao chegou via HTTPS)
app.UseSecurityHeaders(BuildCsp(app.Environment), hsts: !app.Environment.IsDevelopment());
if (corsOrigins.Length > 0) app.UseCors("Configured");
app.UseRateLimiter();
app.UseAuthentication();
app.UseCsrfProtection();
app.UseAuthorization();
app.MapControllers();

// Healthcheck publico (Docker/Render): so informa ok/indisponivel, sem detalhes
app.MapGet("/healthz", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503)).AllowAnonymous();

// /api/* inexistente: 404 (401 sem sessao), nunca o index.html do SPA
app.MapFallback("/api/{**rest}", (HttpContext ctx) => Results.Problem(statusCode: 404, title: "Not Found"));

// Serve Angular static files
var browserPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "browser");
var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
provider.Mappings[".webmanifest"] = "application/manifest+json";

if (Directory.Exists(browserPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(browserPath)
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(browserPath),
        ContentTypeProvider = provider
    });
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(browserPath),
        ContentTypeProvider = provider
    }).AllowAnonymous();
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        ContentTypeProvider = provider
    });
    app.MapFallbackToFile("index.html").AllowAnonymous();
}

// Aplica migrations (EF usa advisory lock, seguro com mais de uma instancia) e seed opcional
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    if (app.Configuration.GetValue<bool>("Seed:Enabled")) DbInitializer.Initialize(db);
    await AdminBootstrap.RunAsync(app.Services);
}

app.Run();

// CSP com o hash dos scripts inline do index.html publicado (se mudar o script, o hash acompanha o build)
static string BuildCsp(IWebHostEnvironment env)
{
    var candidates = new[]
    {
        Path.Combine(env.ContentRootPath, "wwwroot", "browser", "index.html"),
        Path.Combine(env.ContentRootPath, "wwwroot", "index.html")
    };
    var index = candidates.FirstOrDefault(File.Exists);
    var hashes = index is null ? Array.Empty<string>() : SecurityHeaders.InlineScriptHashes(File.ReadAllText(index));
    return SecurityHeaders.BuildCsp(hashes);
}

// Necessario para os testes de integracao (WebApplicationFactory<Program>)
public partial class Program { }
