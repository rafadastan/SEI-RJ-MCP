using System.IO;
using SeiRjMonitor.Domain;
using SeiRjMonitor.Infrastructure.Services;
using Xunit;


namespace SeiRjMonitor.Tests.ParserTests;

public class SeiPageParserTests
{
    [Fact]
    public void DeveExtrairMetadadosDoProcesso()
    {
        var html = File.ReadAllText("Fixtures\\sei_sample.html");
        var resultado = SeiPageParser.Parse(
            html,
            "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc123",
            new DateTimeOffset(2024, 1, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.True(resultado.Sucesso);
        Assert.Null(resultado.Falha);
        Assert.NotNull(resultado.Processo);

        var processo = resultado.Processo!;
        Assert.Equal("SEI-260005/005504/2024", processo.Numero);
        Assert.Equal("Processo", processo.Tipo);
        Assert.Equal("01/01/2024", processo.DataGeracao);
        Assert.Equal("TESTE DA SILVA", processo.Interessados);
        Assert.False(processo.AcessoRestrito);
    }

    [Fact]
    public void DeveExtrairProtocolos()
    {
        var html = File.ReadAllText("Fixtures\\sei_sample.html");
        var resultado = SeiPageParser.Parse(
            html,
            "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc123",
            new DateTimeOffset(2024, 1, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.True(resultado.Sucesso);
        Assert.NotNull(resultado.Processo);

        var processo = resultado.Processo!;

        Assert.Equal(2, processo.QuantidadeProtocolos);
        Assert.Equal(2, processo.Protocolos.Count);

        var p1 = processo.Protocolos[0];
        Assert.Equal("74146277", p1.Numero);
        Assert.Equal("Documento", p1.Natureza);
        Assert.False(p1.AcessoRestrito);

        var p2 = processo.Protocolos[1];
        Assert.Equal("SEI-260005/005446/2026", p2.Numero);
        Assert.Equal("Processo", p2.Natureza);
    }

    [Fact]
    public void DeveExtrairMovimentacoes()
    {
        var html = File.ReadAllText("Fixtures\\sei_sample.html");
        var resultado = SeiPageParser.Parse(
            html,
            "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc123",
            new DateTimeOffset(2024, 1, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.True(resultado.Sucesso);
        Assert.NotNull(resultado.Processo);

        var processo = resultado.Processo!;

        Assert.Equal(3, processo.QuantidadeMovimentacoes);
        Assert.Equal(3, processo.Movimentacoes.Count);

        var m1 = processo.Movimentacoes[0];
        Assert.Equal("FAETEC/PRESI", m1.Unidade);
        Assert.Equal("Recebimento", m1.TipoEvento);

        var m3 = processo.Movimentacoes[2];
        Assert.Equal("Conclusão", m3.TipoEvento);
    }

    [Fact]
    public void DeveRetornarFalhaQuandoProcessoNaoEncontrado()
    {
        var html = "<html><body>Processo não encontrado.</body></html>";
        var resultado = SeiPageParser.Parse(
            html,
            "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc123",
            new DateTimeOffset(2024, 1, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.False(resultado.Sucesso);
        Assert.NotNull(resultado.Falha);
        Assert.Equal(FalhaConsultaTipo.ProcessoNaoEncontrado, resultado.Falha!.Tipo);
    }

    [Fact]
    public void DeveRetornarFalhaQuandoLayoutDesconhecido()
    {
        var html = "<html><body>Página sem dados reconhecíveis.</body></html>";
        var resultado = SeiPageParser.Parse(
            html,
            "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc123",
            new DateTimeOffset(2024, 1, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.False(resultado.Sucesso);
        Assert.NotNull(resultado.Falha);
        Assert.Equal(FalhaConsultaTipo.LayoutDesconhecido, resultado.Falha!.Tipo);
    }
}
