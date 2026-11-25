using Microsoft.EntityFrameworkCore;
using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Domain.Interfaces;
using SeiRjMonitor.Infrastructure.Persistence.Configurations;

namespace SeiRjMonitor.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core para PostgreSQL. Armazena processos, consultas,
/// protocolos e movimentações do monitor SEI-RJ.
/// </summary>
public sealed class SeiRjMonitorDbContext : DbContext, IUnitOfWork
{
    public SeiRjMonitorDbContext(DbContextOptions<SeiRjMonitorDbContext> options)
        : base(options)
    {
    }

    public DbSet<Processo> Processos => Set<Processo>();
    public DbSet<Consulta> Consultas => Set<Consulta>();
    public DbSet<ProtocoloItem> Protocolos => Set<ProtocoloItem>();
    public DbSet<MovimentacaoItem> Movimentacoes => Set<MovimentacaoItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ProcessoConfiguration());
        modelBuilder.ApplyConfiguration(new ConsultaConfiguration());
        modelBuilder.ApplyConfiguration(new ProtocoloConfiguration());
        modelBuilder.ApplyConfiguration(new MovimentacaoConfiguration());
    }

    /// <summary>
    /// Confirma todas as alterações pendentes (unidade de trabalho).
    /// </summary>
    public new Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(cancellationToken);
}
