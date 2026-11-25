using Microsoft.EntityFrameworkCore;
using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Domain.Interfaces;
using SeiRjMonitor.Infrastructure.Persistence;

namespace SeiRjMonitor.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core do repositório de consultas.
/// </summary>
public sealed class ConsultaRepository : IConsultaRepository
{
    private readonly SeiRjMonitorDbContext _context;

    public ConsultaRepository(SeiRjMonitorDbContext context) => _context = context;

    public Task AdicionarAsync(
        Consulta consulta,
        CancellationToken cancellationToken = default)
    {
        _context.Consultas.Add(consulta);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Consulta>> ObterPorProcessoAsync(
        string numero,
        CancellationToken cancellationToken = default)
    {
        return await _context.Consultas
            .Include(c => c.Processo)
            .Where(c => c.Processo.Numero == numero)
            .OrderBy(c => c.ConsultadoEmUtc)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }
}
