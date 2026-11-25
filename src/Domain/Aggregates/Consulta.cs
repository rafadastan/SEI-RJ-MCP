namespace SeiRjMonitor.Domain.Aggregates;

/// <summary>
/// Entidade que representa uma consulta realizada a um processo.
/// </summary>
public class Consulta
{
    protected Consulta()
    {
    }

    public long Id { get; protected set; }
    public long ProcessoId { get; protected set; }
    public Processo Processo { get; protected set; } = default!;
    public DateTimeOffset ConsultadoEmUtc { get; protected set; }
    public string Fonte { get; protected set; } = default!;
    public int? ProtocolosDeclarados { get; protected set; }
    public int? AndamentosDeclarados { get; protected set; }
    public int QuantidadeProtocolos { get; protected set; }
    public int QuantidadeMovimentacoes { get; protected set; }
    public string? UnidadeAtual { get; protected set; }

    public Consulta(
        Processo processo,
        DateTimeOffset consultadoEmUtc,
        string fonte,
        int? protocolosDeclarados,
        int? andamentosDeclarados,
        int quantidadeProtocolos,
        int quantidadeMovimentacoes,
        string? unidadeAtual)
    {
        Processo = processo ?? throw new ArgumentNullException(nameof(processo));
        ProcessoId = processo.Id;
        ConsultadoEmUtc = consultadoEmUtc;
        Fonte = fonte ?? throw new ArgumentNullException(nameof(fonte));
        ProtocolosDeclarados = protocolosDeclarados;
        AndamentosDeclarados = andamentosDeclarados;
        QuantidadeProtocolos = quantidadeProtocolos;
        QuantidadeMovimentacoes = quantidadeMovimentacoes;
        UnidadeAtual = unidadeAtual;
    }
}
