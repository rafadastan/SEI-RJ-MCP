using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SeiRjMonitor.Tests.PresentationTests;

public class SeiToolsTests
{
    [Fact]
    public async Task FerramentaDeveRetornarErroEstruturadoQuandoBancoFalha()
    {
        var tools = new SeiTools(
            new ScopeFactoryComFalha(),
            NullLogger<SeiTools>.Instance);

        var json = await tools.ListarProcessosMonitorados();

        Assert.Contains("FalhaInfraestrutura", json);
        Assert.Contains("banco de dados local", json);
        Assert.DoesNotContain("senha", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FerramentaDevePropagarCancelamentoDoChamador()
    {
        var tools = new SeiTools(
            new ScopeFactoryCancelado(),
            NullLogger<SeiTools>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tools.ListarProcessosMonitorados(cts.Token));
    }

    private sealed class ScopeFactoryComFalha : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new TimeoutException("PostgreSQL indisponível (simulado)");
    }

    private sealed class ScopeFactoryCancelado : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new OperationCanceledException();
    }
}
