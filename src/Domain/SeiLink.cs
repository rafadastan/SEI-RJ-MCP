namespace SeiRjMonitor.Domain;

/// <summary>
/// Link da consulta pública do SEI-RJ já validado estruturalmente.
/// A validação restringe esquema, porta, host e caminho; a resolução DNS e o
/// endereço IP final são validados pela infraestrutura no momento da conexão.
/// </summary>
public sealed record SeiLink(Uri Uri, string FormatoParametro)
{
    private const string CaminhoConsulta = "/sei/modulos/pesquisa/md_pesq_processo_exibir.php";
    private const string DominioBase = "sei.rj.gov.br";

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
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Port != 443
            || !HostPermitido(uri.Host)
            || !string.Equals(uri.AbsolutePath, CaminhoConsulta, StringComparison.OrdinalIgnoreCase))
        {
            erro = "Informe um link HTTPS válido da página pública de consulta processual " +
                   "(md_pesq_processo_exibir.php) no domínio sei.rj.gov.br ou em um de seus subdomínios.";
            return false;
        }

        link = new SeiLink(uri, DetectarFormatoParametro(uri));
        return true;
    }

    private static bool HostPermitido(string host) =>
        host.Equals(DominioBase, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith($".{DominioBase}", StringComparison.OrdinalIgnoreCase);

    private static string DetectarFormatoParametro(Uri uri)
    {
        var query = uri.Query.TrimStart('?');

        if (string.IsNullOrWhiteSpace(query)) return "sem-parametro";
        return query.Contains('=') ? "parametro-nomeado" : "token-opaco";
    }
}
