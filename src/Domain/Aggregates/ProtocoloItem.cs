namespace SeiRjMonitor.Domain.Aggregates;

/// <summary>
/// Entidade que representa um protocolo persistido para um processo.
/// </summary>
public class ProtocoloItem
{
    protected ProtocoloItem()
    {
    }

    public long Id { get; protected set; }
    public long ProcessoId { get; protected set; }
    public string Chave { get; protected set; } = default!;
    public string Numero { get; protected set; } = default!;
    public string Natureza { get; protected set; } = default!;
    public string? Tipo { get; protected set; }
    public string? Data { get; protected set; }
    public string? DataInclusao { get; protected set; }
    public string? Unidade { get; protected set; }
    public bool AcessoRestrito { get; protected set; }
    public DateTimeOffset PrimeiraDeteccaoUtc { get; protected set; }
    public DateTimeOffset UltimaDeteccaoUtc { get; protected set; }

    public ProtocoloItem(
        long processoId,
        string chave,
        Domain.Protocolo protocolo,
        DateTimeOffset detectadoEmUtc)
    {
        ProcessoId = processoId;
        Chave = chave ?? throw new ArgumentNullException(nameof(chave));
        Numero = protocolo.Numero ?? throw new ArgumentNullException(nameof(protocolo.Numero));
        Natureza = protocolo.Natureza ?? throw new ArgumentNullException(nameof(protocolo.Natureza));
        Tipo = protocolo.Tipo;
        Data = protocolo.Data;
        DataInclusao = protocolo.DataInclusao;
        Unidade = protocolo.Unidade;
        AcessoRestrito = protocolo.AcessoRestrito;
        PrimeiraDeteccaoUtc = detectadoEmUtc;
        UltimaDeteccaoUtc = detectadoEmUtc;
    }

    public void AtualizarUltimaDeteccao(DateTimeOffset utc)
    {
        UltimaDeteccaoUtc = utc;
    }
}
