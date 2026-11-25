namespace SeiRjMonitor.Domain.Aggregates;

/// <summary>
/// Raiz do agregado que representa um processo monitorado no SEI-RJ.
/// Encapsula consultas, protocolos e movimentações persistidos localmente.
/// </summary>
public class Processo
{
    private readonly List<Consulta> _consultas = new();
    private readonly List<ProtocoloItem> _protocolos = new();
    private readonly List<MovimentacaoItem> _movimentacoes = new();

    public long Id { get; protected set; }
    public string Numero { get; protected set; } = default!;
    public string? Tipo { get; protected set; }
    public string? DataGeracao { get; protected set; }
    public string? Interessados { get; protected set; }
    public bool AcessoRestrito { get; protected set; }
    public DateTimeOffset PrimeiraConsultaUtc { get; protected set; }
    public DateTimeOffset UltimaConsultaUtc { get; protected set; }
    public string? UnidadeAtual { get; protected set; }

    public IReadOnlyList<Consulta> Consultas => _consultas.AsReadOnly();
    public IReadOnlyList<ProtocoloItem> Protocolos => _protocolos.AsReadOnly();
    public IReadOnlyList<MovimentacaoItem> Movimentacoes => _movimentacoes.AsReadOnly();

    protected Processo()
    {
    }

    public Processo(string numero, DateTimeOffset primeiraConsultaUtc)
    {
        Numero = numero ?? throw new ArgumentNullException(nameof(numero));
        PrimeiraConsultaUtc = primeiraConsultaUtc;
        UltimaConsultaUtc = primeiraConsultaUtc;
    }

    public void AtualizarDados(
        string? tipo,
        string? dataGeracao,
        string? interessados,
        bool acessoRestrito,
        string? unidadeAtual,
        DateTimeOffset ultimaConsultaUtc)
    {
        Tipo = tipo;
        DataGeracao = dataGeracao;
        Interessados = interessados;
        AcessoRestrito = acessoRestrito;
        UnidadeAtual = unidadeAtual;
        UltimaConsultaUtc = ultimaConsultaUtc;
    }

    public Consulta AdicionarConsulta(
        DateTimeOffset consultadoEmUtc,
        string fonte,
        int? protocolosDeclarados,
        int? andamentosDeclarados,
        int quantidadeProtocolos,
        int quantidadeMovimentacoes,
        string? unidadeAtual)
    {
        var consulta = new Consulta(
            this,
            consultadoEmUtc,
            fonte,
            protocolosDeclarados,
            andamentosDeclarados,
            quantidadeProtocolos,
            quantidadeMovimentacoes,
            unidadeAtual);
        _consultas.Add(consulta);
        return consulta;
    }

    public void AdicionarProtocolo(ProtocoloItem protocolo)
    {
        _protocolos.Add(protocolo);
    }

    public void AdicionarMovimentacao(MovimentacaoItem movimentacao)
    {
        _movimentacoes.Add(movimentacao);
    }

    public bool PossuiProtocoloComChave(string chave) =>
        _protocolos.Any(p => p.Chave == chave);

    public bool PossuiMovimentacaoComChave(string chave) =>
        _movimentacoes.Any(m => m.Chave == chave);
}
