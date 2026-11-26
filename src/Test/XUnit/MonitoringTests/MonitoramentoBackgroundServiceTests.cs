using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SeiRjMonitor.Application;
using SeiRjMonitor.Application.Abstractions;
using SeiRjMonitor.Domain;
using SeiRjMonitor.Domain.Interfaces;
using SeiRjMonitor.Infrastructure.Persistence;
using SeiRjMonitor.Infrastructure.Repositories;
using SeiRjMonitor.Infrastructure.Services;
using SeiRjMonitor.Presentation;
using SeiRjMonitor.Tests.Fixtures;
using Xunit;

namespace SeiRjMonitor.Tests.MonitoringTests;

public class MonitoramentoBackgroundServiceTests
{
    private const string NumeroA = "SEI-260005/000001/2024";
    private const string NumeroB = "SEI-260005/000002/2024";
    private const string UrlA =
        "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?aaa";
    private const string UrlB =
        "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?bbb";

    [Fact]
    public async Task CicloDeveConsultarProcessosERegistrarResumo()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderRoteado();
        loader.Definir(UrlA, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento")));
        loader.Definir(UrlB, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroB, "Recebimento")));

        await using var provider = CriarProvider(banco, loader);
        await SemearAsync(provider, UrlA, UrlB);

        loader.Definir(UrlA, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento")));
        loader.Definir(UrlB, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroB, "Recebimento", "Remessa")));

        var servico = CriarBackground(provider);

        await servico.ExecutarCicloAsync(CancellationToken.None);

        Assert.NotNull(servico.UltimoCiclo);
        Assert.Equal(2, servico.UltimoCiclo!.ProcessosConsultados);
        Assert.Equal(0, servico.UltimoCiclo.Falhas);
    }

    [Fact]
    public async Task FalhaEmUmProcessoNaoDeveInterromperOsDemais()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderRoteado();
        loader.Definir(UrlA, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento")));
        loader.Definir(UrlB, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroB, "Recebimento")));

        await using var provider = CriarProvider(banco, loader);
        await SemearAsync(provider, UrlA, UrlB);

        loader.Definir(UrlA, () => throw new HttpRequestException("SEI indisponível"));
        loader.Definir(UrlB, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroB, "Recebimento", "Remessa")));

        var servico = CriarBackground(provider);

        await servico.ExecutarCicloAsync(CancellationToken.None);

        Assert.Equal(1, servico.UltimoCiclo!.Falhas);
        Assert.Equal(2, servico.UltimoCiclo.ProcessosConsultados);

        await using var escopo = provider.CreateAsyncScope();
        var monitoramento = escopo.ServiceProvider.GetRequiredService<SeiMonitoramentoService>();
        var historico = await monitoramento.ObterHistoricoAsync(NumeroB);
        Assert.Equal(2, historico!.Movimentacoes.Count);
    }

    [Fact]
    public async Task IndisponibilidadeDoSeiNaoDeveLancarExcecao()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderRoteado();
        loader.Definir(UrlA, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento")));

        await using var provider = CriarProvider(banco, loader);
        await SemearAsync(provider, UrlA);

        loader.Definir(UrlA, () => PaginaCarregada.Erro(
            FalhaConsultaTipo.FalhaHttp, "Não foi possível conectar ao SEI-RJ no momento."));

        var servico = CriarBackground(provider);

        await servico.ExecutarCicloComProtecaoAsync(CancellationToken.None);

        Assert.Equal(1, servico.UltimoCiclo!.Falhas);
    }

    [Fact]
    public async Task CicloNaoDeveIniciarAntesDoAnteriorTerminar()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderRoteado();
        loader.Definir(UrlA, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento")));

        await using var provider = CriarProvider(banco, loader);
        await SemearAsync(provider, UrlA);

        var iniciado = new TaskCompletionSource();
        var liberar = new TaskCompletionSource();

        loader.DefinirAsync(UrlA, async _ =>
        {
            iniciado.TrySetResult();
            await liberar.Task;
            return PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento"));
        });

        var servico = CriarBackground(provider);

        var primeiro = servico.ExecutarCicloComProtecaoAsync(CancellationToken.None);
        await iniciado.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var segundo = servico.ExecutarCicloComProtecaoAsync(CancellationToken.None);
        await segundo.WaitAsync(TimeSpan.FromSeconds(2));

        liberar.TrySetResult();
        await primeiro.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CicloDeveRespeitarTimeout()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderRoteado();
        loader.Definir(UrlA, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento")));

        await using var provider = CriarProvider(banco, loader);
        await SemearAsync(provider, UrlA);

        loader.DefinirAsync(UrlA, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return PaginaCarregada.Ok("<html></html>");
        });

        var servico = CriarBackground(provider, o => o.CycleTimeoutSeconds = 1);

        var cronometro = Stopwatch.StartNew();
        await servico.ExecutarCicloComProtecaoAsync(CancellationToken.None);
        cronometro.Stop();

        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task CancelamentoNaoDeveConsultarProcessos()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var loader = new LoaderRoteado();
        loader.Definir(UrlA, () => PaginaCarregada.Ok(HtmlSei.Criar(NumeroA, "Recebimento")));

        await using var provider = CriarProvider(banco, loader);
        await SemearAsync(provider, UrlA);

        loader.Contagem = 0;
        var servico = CriarBackground(provider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await servico.ExecutarCicloAsync(cts.Token);

        Assert.Equal(0, loader.Contagem);
    }

    [Fact]
    public async Task IndisponibilidadeDoBancoNaoDeveDerrubarOMonitoramento()
    {
        var servico = new MonitoramentoBackgroundService(
            new ScopeFactoryQuebrado(),
            Options.Create(new MonitoramentoOptions
            {
                IntervalMinutes = 30,
                DelayBetweenProcessesSeconds = 1,
                CycleTimeoutSeconds = 10,
                StartupDelaySeconds = 0
            }),
            NullLogger<MonitoramentoBackgroundService>.Instance);

        await servico.ExecutarCicloComProtecaoAsync(CancellationToken.None);
    }

    private static async Task SemearAsync(IServiceProvider provider, params string[] urls)
    {
        await using var escopo = provider.CreateAsyncScope();
        var monitoramento = escopo.ServiceProvider.GetRequiredService<SeiMonitoramentoService>();

        foreach (var url in urls)
            await monitoramento.ConsultarAsync(url);
    }

    private static MonitoramentoBackgroundService CriarBackground(
        IServiceProvider provider,
        Action<MonitoramentoOptions>? configurar = null)
    {
        var options = new MonitoramentoOptions
        {
            IntervalMinutes = 30,
            DelayBetweenProcessesSeconds = 1,
            CycleTimeoutSeconds = 10,
            StartupDelaySeconds = 0
        };
        configurar?.Invoke(options);

        return new MonitoramentoBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<MonitoramentoBackgroundService>.Instance);
    }

    private static ServiceProvider CriarProvider(BancoTemporario banco, ISeiPageLoader loader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISeiPageLoader>(loader);
        services.AddSingleton<ISeiPageParser, SeiPageParser>();
        services.AddSingleton<SeiRjMonitorDbContext>(banco.Contexto);
        services.AddSingleton<IUnitOfWork>(banco.Contexto);
        services.AddScoped<IProcessoRepository, ProcessoRepository>();
        services.AddScoped<IConsultaRepository, ConsultaRepository>();
        services.AddScoped<IProtocoloRepository, ProtocoloRepository>();
        services.AddScoped<IMovimentacaoRepository, MovimentacaoRepository>();
        services.AddScoped<SeiConsultaService>();
        services.AddScoped<SeiMonitoramentoService>();

        return services.BuildServiceProvider();
    }

    private sealed class LoaderRoteado : ISeiPageLoader
    {
        private readonly Dictionary<string, Func<CancellationToken, Task<PaginaCarregada>>> _respostas = new();

        public int Contagem { get; set; }

        public void Definir(string url, Func<PaginaCarregada> resposta) =>
            _respostas[url] = _ => Task.FromResult(resposta());

        public void DefinirAsync(string url, Func<CancellationToken, Task<PaginaCarregada>> resposta) =>
            _respostas[url] = resposta;

        public Task<PaginaCarregada> CarregarAsync(Uri uri, CancellationToken cancellationToken)
        {
            Contagem++;
            return _respostas[uri.ToString()](cancellationToken);
        }
    }

    private sealed class ScopeFactoryQuebrado : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("PostgreSQL indisponível");
    }
}

