using Npgsql;

namespace backend.Data
{
    /// <summary>
    /// Resolve a connection string do PostgreSQL. Ordem: DATABASE_URL (formato URL do Render,
    /// postgresql://user:pass@host:port/db) e depois ConnectionStrings:Default (formato Npgsql).
    /// </summary>
    public static class DatabaseConnection
    {
        public static string Resolve(IConfiguration config)
        {
            var url = config["DATABASE_URL"];
            if (!string.IsNullOrWhiteSpace(url)) return FromUrl(url);

            var cs = config.GetConnectionString("Default");
            if (!string.IsNullOrWhiteSpace(cs)) return cs;

            throw new InvalidOperationException(
                "Banco nao configurado: defina DATABASE_URL (postgresql://user:pass@host:5432/db) ou ConnectionStrings:Default.");
        }

        public static string FromUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "postgresql" && uri.Scheme != "postgres"))
                throw new InvalidOperationException("DATABASE_URL invalida: esperado postgresql://user:pass@host:port/database.");

            var userInfo = uri.UserInfo.Split(':', 2);
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null
            };

            // sslmode da URL; sem ele, exige TLS exceto em localhost (dev). O certificado interno do Render
            // e autoassinado, e o Npgsql (>= 6) valida a cadeia em SslMode.Require; por isso TrustServerCertificate=true
            // reproduz o "require" do libpq (criptografa sem validar a cadeia). Nao protege contra MITM ativo na rede interna.
            var sslMode = System.Web.HttpUtility.ParseQueryString(uri.Query)["sslmode"];
            var isLocal = uri.Host is "localhost" or "127.0.0.1" or "::1";
            builder.SslMode = sslMode?.ToLowerInvariant() switch
            {
                "disable" => SslMode.Disable,
                "prefer" => SslMode.Prefer,
                "require" => SslMode.Require,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                _ => isLocal ? SslMode.Disable : SslMode.Require
            };

            if (builder.SslMode == SslMode.Require) builder.TrustServerCertificate = true;

            return builder.ConnectionString;
        }
    }
}
