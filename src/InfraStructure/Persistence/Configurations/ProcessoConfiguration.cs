using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeiRjMonitor.Domain.Aggregates;

namespace SeiRjMonitor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF Core para a entidade Processo.
/// </summary>
public sealed class ProcessoConfiguration : IEntityTypeConfiguration<Processo>
{
    public void Configure(EntityTypeBuilder<Processo> builder)
    {
        builder.ToTable("processos");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.Numero)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(p => p.Numero)
            .IsUnique()
            .HasDatabaseName("ux_processos_numero");

        builder.Property(p => p.Tipo).HasMaxLength(100);
        builder.Property(p => p.DataGeracao).HasMaxLength(50);
        builder.Property(p => p.Interessados).HasMaxLength(500);
        builder.Property(p => p.UnidadeAtual).HasMaxLength(200);

        builder.Property(p => p.AcessoRestrito).IsRequired();
        builder.Property(p => p.PrimeiraConsultaUtc).IsRequired();
        builder.Property(p => p.UltimaConsultaUtc).IsRequired();

        builder.HasMany(p => p.Consultas)
            .WithOne(c => c.Processo)
            .HasForeignKey(c => c.ProcessoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Protocolos)
            .WithOne()
            .HasForeignKey("ProcessoId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Movimentacoes)
            .WithOne()
            .HasForeignKey("ProcessoId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
