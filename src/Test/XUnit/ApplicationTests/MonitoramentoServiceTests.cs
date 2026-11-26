using SeiRjMonitor.Application;
using SeiRjMonitor.Application.Abstractions;
using SeiRjMonitor.Domain;
using SeiRjMonitor.Infrastructure.Repositories;
using SeiRjMonitor.Infrastructure.Services;
using SeiRjMonitor.Tests.Fixtures;
using Xunit;

namespace SeiRjMonitor.Tests.ApplicationTests;

public class MonitoramentoServiceTests
{
    private const string Numero = "SEI-260005/005504/2024";
    private const string Url =
        "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc123";

    [Fact]
    public async Task PrimeiraConsultaDeveRegistrarLinhaDeBaseSemNovidades()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderFixo(HtmlSei.Criar(Numero, "Recebimento do processo"));
        var servico = CriarServico(banco, loader);

        var consulta = await servico.ConsultarAsync(Url);

        Assert.True(consulta.Resultado.Sucesso);
        Assert.NotNull(consulta.Persistencia);
        Assert.True(consulta.Persistencia!.PrimeiraConsulta);
        Assert.Equal(0, consulta.Persistencia.MovimentacoesNovas);
        Assert.Equal(1, consulta.Persistencia.MovimentacoesTotal);
        Assert.Empty(consulta.Persistencia.NovasMovimentacoes);
    }

    [Fact]
    public async Task ConsultaRepetidaNaoDeveDuplicarMovimentacoes()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderFixo(HtmlSei.Criar(Numero, "Recebimento do processo"));
        var servico = CriarServico(banco, loader);

        await servico.ConsultarAsync(Url);
        var segunda = await servico.ConsultarAsync(Url);

        Assert.False(segunda.Persistencia!.PrimeiraConsulta);
        Assert.Equal(0, segunda.Persistencia.MovimentacoesNovas);
        Assert.Equal(1, segunda.Persistencia.MovimentacoesTotal);

        var historico = await servico.ObterHistoricoAsync(Numero);
        Assert.NotNull(historico);
        Assert.Equal(2, historico!.TotalConsultas);
        Assert.Single(historico.Movimentacoes);
    }

    [Fact]
    public async Task DeveDetectarMovimentacaoNova()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderFixo(HtmlSei.Criar(Numero, "Recebimento do processo"));
        var servico = CriarServico(banco, loader);

        await servico.ConsultarAsync(Url);

        loader.Html = HtmlSei.Criar(Numero, "Recebimento do processo", "Remessa para unidade");
        var segunda = await servico.ConsultarAsync(Url);

        Assert.Equal(1, segunda.Persistencia!.MovimentacoesNovas);
        var nova = Assert.Single(segunda.Persistencia.NovasMovimentacoes);
        Assert.Equal("Remessa para unidade", nova.Descricao);

        var historico = await servico.ObterHistoricoAsync(Numero);
        Assert.Equal(2, historico!.Movimentacoes.Count);
    }

    [Fact]
    public async Task DeveListarProcessosMonitorados()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderFixo(HtmlSei.Criar(Numero, "Recebimento"));
        var servico = CriarServico(banco, loader);

        await servico.ConsultarAsync(Url);
        await servico.ConsultarAsync(Url);

        var processos = await servico.ListarProcessosAsync();

        var processo = Assert.Single(processos);
        Assert.Equal(Numero, processo.Numero);
        Assert.Equal(2, processo.TotalConsultas);
        Assert.Equal(1, processo.TotalMovimentacoes);
    }

    [Fact]
    public async Task DeveListarProcessoComLinkDaConsultaMaisRecente()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderFixo(HtmlSei.Criar(Numero, "Recebimento"));
        var servico = CriarServico(banco, loader);

        await servico.ConsultarAsync(Url);

        var agendados = await servico.ListarProcessosParaMonitorarAsync();

        var agendado = Assert.Single(agendados);
        Assert.Equal(Numero, agendado.Numero);
        Assert.Equal(Url, agendado.Url);
    }

    [Fact]
    public async Task ProcessoNaoEncontradoNaoDevePersistir()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderFixo("<html><body>Processo não encontrado.</body></html>");
        var servico = CriarServico(banco, loader);

        var consulta = await servico.ConsultarAsync(Url);

        Assert.False(consulta.Resultado.Sucesso);
        Assert.Null(consulta.Persistencia);
        Assert.Empty(await servico.ListarProcessosAsync());
    }

    [Fact]
    public async Task HistoricoDeProcessoInexistenteDeveRetornarNulo()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var servico = CriarServico(banco, new LoaderFixo(""));

        Assert.Null(await servico.ObterHistoricoAsync("SEI-000000/000000/2024"));
    }

    private static SeiMonitoramentoService CriarServico(
        BancoTemporario banco,
        ISeiPageLoader loader)
    {
        var ctx = banco.Contexto;
        var consulta = new SeiConsultaService(loader, new SeiPageParser());

        return new SeiMonitoramentoService(
            consulta,
            new ProcessoRepository(ctx),
            new ConsultaRepository(ctx),
            new ProtocoloRepository(ctx),
            new MovimentacaoRepository(ctx),
            ctx);
    }

    private sealed class LoaderFixo : ISeiPageLoader
    {
        public LoaderFixo(string html) => Html = html;

        public string Html { get; set; }

        public Task<PaginaCarregada> CarregarAsync(Uri uri, CancellationToken cancellationToken) =>
            Task.FromResult(PaginaCarregada.Ok(Html));
    }
}
