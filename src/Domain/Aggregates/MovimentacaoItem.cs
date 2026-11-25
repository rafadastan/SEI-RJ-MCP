namespace SeiRjMonitor.Domain.Aggregates;

/// <summary>
/// Entidade que representa uma movimentação persistida para um processo.
/// </summary>
public class MovimentacaoItem
{
    protected MovimentacaoItem()
    {
    }

    public long Id { get; protected set; }
    public long ProcessoId { get; protected set; }
    public long ConsultaId { get; protected set; }
    public string Chave { get; protected set; } = default!;
    public DateTime DataHora { get; protected set; }
    public string Unidade { get; protected set; } = default!;
    public string? UnidadeRelacionada { get; protected set; }
    public string TipoEvento { get; protected set; } = default!;
    public string Descricao { get; protected set; } = default!;
    public DateTimeOffset DetectadaEmUtc { get; protected set; }

    public MovimentacaoItem(
        long processoId,
        long consultaId,
        string chave,
        Domain.Movimentacao movimentacao,
        DateTimeOffset detectadaEmUtc)
    {
        ProcessoId = processoId;
        ConsultaId = consultaId;
        Chave = chave ?? throw new ArgumentNullException(nameof(chave));
        DataHora = movimentacao.DataHora;
        Unidade = movimentacao.Unidade ?? throw new ArgumentNullException(nameof(movimentacao.Unidade));
        UnidadeRelacionada = movimentacao.UnidadeRelacionada;
        TipoEvento = movimentacao.TipoEvento ?? throw new ArgumentNullException(nameof(movimentacao.TipoEvento));
        Descricao = movimentacao.Descricao ?? throw new ArgumentNullException(nameof(movimentacao.Descricao));
        DetectadaEmUtc = detectadaEmUtc;
    }
}
