using Microsoft.Extensions.Diagnostics.HealthChecks;
using SeiRjMonitor.Infrastructure.Persistence;

namespace SeiRjMonitor.Presentation;

/// <summary>
/// Readiness: verifica apenas se o PostgreSQL responde, com timeout curto.
/// Não executa consultas de negócio nem expõe detalhes do erro.
/// </summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;

    public PostgresHealthCheck(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);

        try
        {
            await using var escopo = _scopeFactory.CreateAsyncScope();
            var db = escopo.ServiceProvider.GetRequiredService<SeiRjMonitorDbContext>();

            return await db.Database.CanConnectAsync(cts.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL indisponível.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Timeout ao verificar o PostgreSQL.");
        }
        catch (Exception)
        {
            // A mensagem original pode conter host/usuário; não é propagada.
            return HealthCheckResult.Unhealthy("Falha ao verificar o PostgreSQL.");
        }
    }
}
