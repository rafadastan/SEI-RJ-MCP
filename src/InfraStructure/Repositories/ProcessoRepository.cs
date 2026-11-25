using Microsoft.EntityFrameworkCore;
using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Domain.Interfaces;
using SeiRjMonitor.Infrastructure.Persistence;

namespace SeiRjMonitor.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core do repositório de processos.
/// </summary>
public sealed class ProcessoRepository : IProcessoRepository
{
    private readonly SeiRjMonitorDbContext _context;

    public ProcessoRepository(SeiRjMonitorDbContext context) => _context = context;

    public async Task<Processo?> ObterPorNumeroAsync(
        string numero,
        CancellationToken cancellationToken = default)
    {
        // AsSplitQuery evita o produto cartesiano entre as três coleções
        // (consultas x protocolos x movimentações), que cresce rápido com o histórico.
        return await _context.Processos
            .Include(p => p.Consultas)
            .Include(p => p.Protocolos)
            .Include(p => p.Movimentacoes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Numero == numero, cancellationToken);
    }

    public Task AdicionarAsync(
        Processo processo,
        CancellationToken cancellationToken = default)
    {
        _context.Processos.Add(processo);
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(
        Processo processo,
        CancellationToken cancellationToken = default)
    {
        _context.Processos.Update(processo);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ProcessoResumo>> ListarResumosAsync(
        CancellationToken cancellationToken = default)
    {
        // A ordenação precisa ocorrer antes da projeção para um record:
        // o EF Core não traduz acesso a membro da própria projeção no ORDER BY.
        return await _context.Processos
            .OrderByDescending(p => p.UltimaConsultaUtc)
            .ThenByDescending(p => p.Id)
            .Select(p => new ProcessoResumo(
                p.Numero,
                p.Tipo,
                p.PrimeiraConsultaUtc,
                p.UltimaConsultaUtc,
                p.Consultas.Count,
                p.Movimentacoes.Count,
                p.UnidadeAtual))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessoParaAgendamento>> ListarParaAgendamentoAsync(
        CancellationToken cancellationToken = default)
    {
        var ultimaConsultaPorProcesso = _context.Consultas
            .GroupBy(c => c.ProcessoId)
            .Select(g => new
            {
                ProcessoId = g.Key,
                Fonte = g.OrderByDescending(c => c.ConsultadoEmUtc)
                         .ThenByDescending(c => c.Id)
                         .First().Fonte
            });

        return await _context.Processos
            .Join(
                ultimaConsultaPorProcesso,
                p => p.Id,
                lc => lc.ProcessoId,
                (p, lc) => new { p, lc })
            .OrderBy(x => x.p.UltimaConsultaUtc)
            .ThenBy(x => x.p.Id)
            .Select(x => new ProcessoParaAgendamento(x.p.Numero, x.lc.Fonte))
            .ToListAsync(cancellationToken);
    }
}
