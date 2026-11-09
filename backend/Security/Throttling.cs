using System.Collections.Concurrent;
using backend.Data;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Security
{
    /// <summary>
    /// Contadores de falha por NOME de usuario (exista ou nao: mesmo comportamento = sem enumeracao).
    /// Login e ativacao tem contadores separados. 5 falhas -> bloqueio de 15 min.
    /// </summary>
    public class AuthThrottleService
    {
        public const string Login = "login";
        public const string Activate = "ativar";
        public const int MaxFailures = 5;
        public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

        private readonly AppDbContext _db;
        public AuthThrottleService(AppDbContext db) => _db = db;

        /// <summary>Segundos restantes de bloqueio, ou nulo se liberado.</summary>
        public async Task<int?> LockedSecondsAsync(string kind, string key, CancellationToken ct = default)
        {
            var until = await _db.AuthThrottles.AsNoTracking()
                .Where(t => t.Kind == kind && t.Key == key)
                .Select(t => t.LockedUntil).FirstOrDefaultAsync(ct);
            if (until is null || until <= DateTime.UtcNow) return null;
            return Math.Max(1, (int)Math.Ceiling((until.Value - DateTime.UtcNow).TotalSeconds));
        }

        public async Task RegisterFailureAsync(string kind, string key, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var windowStart = now - LockDuration;
            // upsert atomico: falhas antigas (fora da janela) recomecam a contagem
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO ""AuthThrottles"" (""Kind"", ""Key"", ""Failures"", ""UpdatedAt"", ""LockedUntil"")
VALUES ({kind}, {key}, 1, {now}, NULL)
ON CONFLICT (""Kind"", ""Key"") DO UPDATE SET
  ""Failures"" = CASE WHEN ""AuthThrottles"".""UpdatedAt"" < {windowStart} THEN 1 ELSE ""AuthThrottles"".""Failures"" + 1 END,
  ""UpdatedAt"" = {now}", ct);

            var lockUntil = now + LockDuration;
            await _db.AuthThrottles
                .Where(t => t.Kind == kind && t.Key == key && t.Failures >= MaxFailures)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LockedUntil, lockUntil).SetProperty(t => t.Failures, 0), ct);
        }

        public Task ResetAsync(string kind, string key, CancellationToken ct = default) =>
            _db.AuthThrottles.Where(t => t.Kind == kind && t.Key == key).ExecuteDeleteAsync(ct);

        public Task ResetAllAsync(string key, CancellationToken ct = default) =>
            _db.AuthThrottles.Where(t => t.Key == key).ExecuteDeleteAsync(ct);
    }

    /// <summary>Limite por IP que conta so FALHAS (login/ativacao/troca de senha): padrao 5 por minuto. Em memoria, por instancia.</summary>
    public class IpFailureLimiter
    {
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _failures = new();
        private readonly int _limit;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        public IpFailureLimiter(IConfiguration config) => _limit = config.GetValue("RateLimit:LoginFailuresPerMinute", 5);

        /// <summary>Segundos ate liberar, ou nulo se o IP pode tentar.</summary>
        public int? BlockedSeconds(string ip)
        {
            if (!_failures.TryGetValue(ip, out var q)) return null;
            lock (q)
            {
                Trim(q);
                if (q.Count < _limit) return null;
                return Math.Max(1, (int)Math.Ceiling((q.Peek() + Window - DateTime.UtcNow).TotalSeconds));
            }
        }

        public void RecordFailure(string ip)
        {
            if (_failures.Count > 10_000) Sweep();
            var q = _failures.GetOrAdd(ip, _ => new Queue<DateTime>());
            lock (q) { Trim(q); q.Enqueue(DateTime.UtcNow); }
        }

        private static void Trim(Queue<DateTime> q)
        {
            var min = DateTime.UtcNow - Window;
            while (q.Count > 0 && q.Peek() < min) q.Dequeue();
        }

        private void Sweep()
        {
            foreach (var (ip, q) in _failures)
            {
                lock (q) { Trim(q); if (q.Count == 0) _failures.TryRemove(ip, out _); }
            }
        }
    }

    public class AuditService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public AuditService(AppDbContext db, IHttpContextAccessor http) { _db = db; _http = http; }

        public static string? Clean(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);

        /// <summary>Registra um evento. Nunca recebe senha, codigo, hash, cookie ou corpo de request.</summary>
        public async Task LogAsync(string evento, string resultado, string? ator, string? alvo, string? detalhe = null)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Evento = evento,
                Resultado = resultado,
                AtorUsuario = Clean(ator, 32),
                AlvoUsuario = Clean(alvo, 32),
                Ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                Detalhe = Clean(detalhe, 200)
            });
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>Retencao: auditoria (inclui IP) por 90 dias; contadores de bloqueio antigos sao limpos.</summary>
    public class RetentionService : BackgroundService
    {
        public static readonly TimeSpan AuditRetention = TimeSpan.FromDays(90);
        private readonly IServiceProvider _services;
        private readonly ILogger<RetentionService> _log;

        public RetentionService(IServiceProvider services, ILogger<RetentionService> log) { _services = services; _log = log; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PurgeAsync(_services, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "Falha ao limpar auditoria/contadores antigos");
                }
                try { await Task.Delay(TimeSpan.FromHours(6), stoppingToken); }
                catch (OperationCanceledException) { }
            }
        }

        public static async Task<int> PurgeAsync(IServiceProvider services, CancellationToken ct)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow - AuditRetention;
            var removed = await db.AuditLogs.Where(a => a.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
            var throttleCutoff = DateTime.UtcNow - TimeSpan.FromDays(1);
            await db.AuthThrottles.Where(t => t.UpdatedAt < throttleCutoff && (t.LockedUntil == null || t.LockedUntil < DateTime.UtcNow))
                .ExecuteDeleteAsync(ct);
            return removed;
        }
    }
}
