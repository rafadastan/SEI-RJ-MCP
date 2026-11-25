namespace SeiRjMonitor.Domain;

/// <summary>
/// Link da consulta pública do SEI-RJ já validado estruturalmente.
/// </summary>
public sealed record SeiLink(Uri Uri, string FormatoParametro)
{
    private const string CaminhoConsulta = "/sei/modulos/pesquisa/md_pesq_processo_exibir.php";

    public bool PossuiParametroDeConsulta => !string.IsNullOrWhiteSpace(Uri.Query.TrimStart('?'));

    public static bool TryParse(string? value, out SeiLink? link, out string erro)
    {
        link = null;
        erro = string.Empty;

        // O SEI-RJ publica consultas tanto em sei.rj.gov.br quanto em subdomínios
        // (por exemplo, baixada.sei.rj.gov.br).
        var valido = Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri is not null;

        if (!valido
            || uri!.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("sei.rj.gov.br", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.EndsWith(".sei.rj.gov.br", StringComparison.OrdinalIgnoreCase))
            || !uri.AbsolutePath.Contains(CaminhoConsulta, StringComparison.OrdinalIgnoreCase))
        {
            erro = "Informe um link HTTPS válido da página pública de consulta processual (md_pesq_processo_exibir.php) " +
                   "no domínio sei.rj.gov.br ou em um de seus subdomínios.";
            return false;
        }

        link = new SeiLink(uri, DetectarFormatoParametro(uri));
        return true;
    }

    private static string DetectarFormatoParametro(Uri uri)
    {
        var query = uri.Query.TrimStart('?');

        if (string.IsNullOrWhiteSpace(query)) return "sem-parametro";
        return query.Contains('=') ? "parametro-nomeado" : "token-opaco";
    }
}
