using Microsoft.EntityFrameworkCore;
using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Domain.Interfaces;
using SeiRjMonitor.Infrastructure.Persistence;

namespace SeiRjMonitor.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core do repositório de movimentações.
/// </summary>
public sealed class MovimentacaoRepository : IMovimentacaoRepository
{
    private readonly SeiRjMonitorDbContext _context;

    public MovimentacaoRepository(SeiRjMonitorDbContext context) => _context = context;

    public async Task<bool> ExisteChaveAsync(
        string chave,
        CancellationToken cancellationToken = default)
    {
        return await _context.Movimentacoes
            .AnyAsync(m => m.Chave == chave, cancellationToken);
    }
}
