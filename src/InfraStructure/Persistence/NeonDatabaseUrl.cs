using Npgsql;

namespace SeiRjMonitor.Infrastructure.Persistence;

/// <summary>
/// Converte a URL de conexão fornecida pelo Neon
/// (<c>DATABASE_URL</c> / <c>DATABASE_URL_UNPOOLED</c>), no formato
/// <c>postgresql://usuario:senha@host/banco?sslmode=require</c>, para o
/// formato chave=valor aceito pelo Npgsql. A senha nunca é registrada.
/// </summary>
public static class NeonDatabaseUrl
{
    /// <summary>
    /// Devolve a connection string do Npgsql ou <c>null</c> quando o valor
    /// não é uma URL PostgreSQL válida.
    /// </summary>
    public static string? ParaConnectionString(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (!string.Equals(uri.Scheme, "postgres", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "postgresql", StringComparison.OrdinalIgnoreCase))
            return null;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Timeout = 10,
            // O Neon exige TLS; a URL já traz sslmode=require.
            SslMode = SslMode.Require
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && !string.IsNullOrEmpty(userInfo[0]))
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1)
            builder.Password = Uri.UnescapeDataString(userInfo[1]);

        foreach (var parametro in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = parametro.Split('=', 2);
            var chave = Uri.UnescapeDataString(partes[0]);
            var valor = partes.Length > 1 ? Uri.UnescapeDataString(partes[1]) : string.Empty;

            switch (chave.ToLowerInvariant())
            {
                case "sslmode" when Enum.TryParse<SslMode>(valor, ignoreCase: true, out var sslMode):
                    builder.SslMode = sslMode;
                    break;
                case "application_name":
                    builder.ApplicationName = valor;
                    break;
                case "options":
                    builder.Options = valor;
                    break;
            }
        }

        return builder.ConnectionString;
    }
}
