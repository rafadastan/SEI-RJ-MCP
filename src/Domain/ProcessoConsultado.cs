namespace SeiRjMonitor.Domain;

/// <summary>
/// Resultado de uma consulta pública bem-sucedida ao SEI-RJ.
/// </summary>
public sealed record ProcessoConsultado(
    string Fonte,
    DateTimeOffset ConsultadoEmUtc,
    string? Numero,
    string? Tipo,
    string? DataGeracao,
    string? Interessados,
    bool AcessoRestrito,
    int? ProtocolosDeclarados,
    int? AndamentosDeclarados,
    IReadOnlyList<Protocolo> Protocolos,
    IReadOnlyList<Movimentacao> Movimentacoes)
{
    public int QuantidadeProtocolos => Protocolos.Count;

    public int ProtocolosComRestricao => Protocolos.Count(p => p.AcessoRestrito);

    public int QuantidadeMovimentacoes => Movimentacoes.Count;

    public Movimentacao? UltimaMovimentacao =>
        Movimentacoes.Count == 0 ? null : Movimentacoes[^1];

    public string? UnidadeAtual => UltimaMovimentacao?.Unidade;

    public DateTime? UltimaMovimentacaoEm => UltimaMovimentacao?.DataHora;
}
