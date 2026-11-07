using backend.Data;
using Microsoft.EntityFrameworkCore;

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

// PostgreSQL (DATABASE_URL no Render; ConnectionStrings:Default em dev/testes)
var connectionString = DatabaseConnection.Resolve(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
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
app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// Healthcheck publico (Docker/Render): so informa ok/indisponivel, sem detalhes
app.MapGet("/healthz", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));

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
    });
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        ContentTypeProvider = provider
    });
    app.MapFallbackToFile("index.html");
}

// Aplica migrations (EF usa advisory lock, seguro com mais de uma instancia) e seed opcional
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    if (app.Configuration.GetValue<bool>("Seed:Enabled")) DbInitializer.Initialize(db);
}

app.Run();

// Necessario para os testes de integracao (WebApplicationFactory<Program>)
public partial class Program { }
