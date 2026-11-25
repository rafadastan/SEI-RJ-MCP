namespace SeiRjMonitor.Domain;

/// <summary>
/// Protocolo listado na página pública do processo. Pode ser um documento
/// interno (somente dígitos) ou um processo vinculado (SEI-...).
/// </summary>
public sealed record Protocolo(
    string Numero,
    string Natureza,
    string? Tipo,
    string? Data,
    string? DataInclusao,
    string? Unidade,
    bool AcessoRestrito);
