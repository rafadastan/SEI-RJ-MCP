namespace SeiRjMonitor.Domain;

/// <summary>
/// Andamento exibido no histórico público do processo.
/// </summary>
public sealed record Movimentacao(
    DateTime DataHora,
    string Unidade,
    string? UnidadeRelacionada,
    string TipoEvento,
    string Descricao);
