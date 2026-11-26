using Microsoft.EntityFrameworkCore;
using SeiRjMonitor.Infrastructure.Persistence;

namespace SeiRjMonitor.Tests.Fixtures;

/// <summary>
/// Banco isolado em memória para testes de aplicação e repositório. Consultas
/// são executadas em LINQ to Objects, evitando limitações do provider de teste
/// que não existem no PostgreSQL de produção.
/// </summary>
internal sealed class BancoTemporario : IAsyncDisposable
{
    private BancoTemporario(SeiRjMonitorDbContext contexto) => Contexto = contexto;

    public SeiRjMonitorDbContext Contexto { get; }

    public static async Task<BancoTemporario> CriarAsync()
    {
        var options = new DbContextOptionsBuilder<SeiRjMonitorDbContext>()
            .UseInMemoryDatabase($"sei-monitor-tests-{Guid.NewGuid():N}")
            .Options;

        var contexto = new SeiRjMonitorDbContext(options);
        await contexto.Database.EnsureCreatedAsync();

        return new BancoTemporario(contexto);
    }

    public async ValueTask DisposeAsync() => await Contexto.DisposeAsync();
}
