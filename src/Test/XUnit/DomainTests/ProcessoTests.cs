using SeiRjMonitor.Domain;
using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Domain.ValueObjects;
using Xunit;

namespace SeiRjMonitor.Tests.DomainTests;

public class ProcessoTests
{
    [Fact]
    public void DeveCriarProcessoComPrimeiraConsulta()
    {
        var quando = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var processo = new Processo("SEI-260005/005504/2024", quando);

        Assert.Equal("SEI-260005/005504/2024", processo.Numero);
        Assert.Equal(quando, processo.PrimeiraConsultaUtc);
        Assert.Equal(quando, processo.UltimaConsultaUtc);
        Assert.Empty(processo.Consultas);
        Assert.Empty(processo.Protocolos);
        Assert.Empty(processo.Movimentacoes);
    }

    [Fact]
    public void NumeroProcessoCriarComValorValidoDeveRetornarVO()
    {
        var numero = NumeroProcesso.Criar("SEI-260005/005504/2024");
        Assert.NotNull(numero);
        Assert.Equal("SEI-260005/005504/2024", numero!.Valor);
    }

    [Fact]
    public void NumeroProcessoCriarComValorNuloOuVazioDeveRetornarNulo()
    {
        Assert.Null(NumeroProcesso.Criar(null));
        Assert.Null(NumeroProcesso.Criar(""));
        Assert.Null(NumeroProcesso.Criar("   "));
    }

    [Fact]
    public void DeveAdicionarConsultaAoProcesso()
    {
        var quando = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var processo = new Processo("SEI-260005/005504/2024", quando);

        var consulta = processo.AdicionarConsulta(
            quando,
            "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc",
            2,
            3,
            2,
            3,
            "FAETEC/PRESI");

        Assert.Single(processo.Consultas);
        Assert.Same(consulta, processo.Consultas[0]);
        Assert.Equal(2, consulta.QuantidadeProtocolos);
        Assert.Equal(3, consulta.QuantidadeMovimentacoes);
    }

    [Fact]
    public void DeveAtualizarDadosDoProcesso()
    {
        var quando = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var processo = new Processo("SEI-260005/005504/2024", quando);

        var depois = new DateTimeOffset(2024, 1, 2, 11, 0, 0, TimeSpan.Zero);
        processo.AtualizarDados(
            "Tipo X",
            "02/01/2024",
            "Interessado Y",
            true,
            "UNIDADE/Z",
            depois);

        Assert.Equal("Tipo X", processo.Tipo);
        Assert.Equal("02/01/2024", processo.DataGeracao);
        Assert.Equal("Interessado Y", processo.Interessados);
        Assert.True(processo.AcessoRestrito);
        Assert.Equal("UNIDADE/Z", processo.UnidadeAtual);
        Assert.Equal(depois, processo.UltimaConsultaUtc);
    }

    [Fact]
    public void DeveDetectarChaveDeProtocoloExistente()
    {
        var quando = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var processo = new Processo("SEI-260005/005504/2024", quando);

        var protocolo = new Domain.Protocolo(
            Numero: "74146277",
            Natureza: "Documento",
            Tipo: "Documento",
            Data: "15/01/2024",
            DataInclusao: "15/01/2024 10:00",
            Unidade: "FAETEC/PRESI",
            AcessoRestrito: false);

        var chave = CalcularChaveProtocolo(protocolo);
        processo.AdicionarProtocolo(new ProtocoloItem(processo.Id, chave, protocolo, quando));

        Assert.True(processo.PossuiProtocoloComChave(chave));
        Assert.False(processo.PossuiProtocoloComChave("outra-chave"));
    }

    [Fact]
    public void DeveDetectarChaveDeMovimentacaoExistente()
    {
        var quando = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var processo = new Processo("SEI-260005/005504/2024", quando);

        var movimentacao = new Domain.Movimentacao(
            DataHora: new DateTime(2024, 1, 10, 9, 0, 0),
            Unidade: "FAETEC/PRESI",
            UnidadeRelacionada: null,
            TipoEvento: "Recebimento",
            Descricao: "Recebimento do processo");

        var chave = CalcularChaveMovimentacao(movimentacao);
        processo.AdicionarMovimentacao(new MovimentacaoItem(processo.Id, 1, chave, movimentacao, quando));

        Assert.True(processo.PossuiMovimentacaoComChave(chave));
        Assert.False(processo.PossuiMovimentacaoComChave("outra-chave"));
    }

    private static string CalcularChaveProtocolo(Domain.Protocolo p) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                string.Join('\u001f', p.Numero, p.Natureza, p.Tipo, p.Data, p.DataInclusao, p.Unidade, p.AcessoRestrito ? "1" : "0"))))
            .ToLowerInvariant();

    private static string CalcularChaveMovimentacao(Domain.Movimentacao m) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                string.Join('\u001f', m.DataHora.ToString("O", System.Globalization.CultureInfo.InvariantCulture), m.Unidade, m.UnidadeRelacionada, m.TipoEvento, m.Descricao))))
            .ToLowerInvariant();
}
