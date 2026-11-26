using Microsoft.EntityFrameworkCore;
using SeiRjMonitor.Domain.Interfaces;
using SeiRjMonitor.Infrastructure.Persistence;

namespace SeiRjMonitor.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core do repositório de protocolos.
/// </summary>
public sealed class ProtocoloRepository : IProtocoloRepository
{
    private readonly SeiRjMonitorDbContext _context;

    public ProtocoloRepository(SeiRjMonitorDbContext context) => _context = context;

    public async Task<bool> ExisteChaveAsync(
        long processoId,
        string chave,
        CancellationToken cancellationToken = default)
    {
        return await _context.Protocolos
            .AnyAsync(
                p => p.ProcessoId == processoId && p.Chave == chave,
                cancellationToken);
    }
}
