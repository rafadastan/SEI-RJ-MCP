using System.Globalization;
using System.Text.RegularExpressions;

namespace SeiRjMonitor.Domain;

/// <summary>
/// Normalização e leitura de textos do SEI-RJ.
/// </summary>
public static partial class SeiTextos
{
    private static readonly string[] FormatosDataHora =
    {
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy"
    };

    // Formatos de número de processo já observados no SEI-RJ:
    // - SEI-260005/005504/2024 (padrão SEI)
    // - 16101.000154/2024.89  (padrão legado)
    [GeneratedRegex(@"SEI-\d{6}/\d{6}/\d{4}", RegexOptions.IgnoreCase)]
    private static partial Regex NumeroProcessoSeiRegex();

    [GeneratedRegex(@"\b\d{5}\.\d{6}/\d{4}\.\d{2}\b")]
    private static partial Regex NumeroProcessoLegadoRegex();

    // Um protocolo listado pode ser um documento interno (somente dígitos)
    // ou um processo vinculado (SEI-260005/005446/2026).
    [GeneratedRegex(@"^(?:\d{5,}|SEI-\d{6}/\d{6}/\d{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex NumeroProtocoloRegex();

    [GeneratedRegex(@"(\d+)\s+registros?", RegexOptions.IgnoreCase)]
    private static partial Regex RegistrosRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosRegex();

    public static string Normalizar(string? value) =>
        EspacosRegex().Replace(value ?? string.Empty, " ").Trim();

    public static string? NuloSeVazio(string? value)
    {
        var normalized = Normalizar(value);
        return normalized.Length == 0 ? null : normalized;
    }

    public static bool EhNumeroProtocolo(string numero) =>
        NumeroProtocoloRegex().IsMatch(numero);

    public static string? ExtrairNumeroProcesso(string texto)
    {
        var match = NumeroProcessoSeiRegex().Match(texto);
        if (match.Success) return match.Value;

        match = NumeroProcessoLegadoRegex().Match(texto);
        return match.Success ? match.Value : null;
    }

    public static int? ExtrairQuantidadeRegistros(string? caption)
    {
        var match = RegistrosRegex().Match(Normalizar(caption));
        return match.Success && int.TryParse(match.Groups[1].Value, out var count)
            ? count
            : null;
    }

    public static bool TryParseDataHora(string value, out DateTime dataHora) =>
        DateTime.TryParseExact(
            value,
            FormatosDataHora,
            CultureInfo.GetCultureInfo("pt-BR"),
            DateTimeStyles.None,
            out dataHora);
}
