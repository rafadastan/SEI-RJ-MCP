using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeiRjMonitor.Domain.Aggregates;

namespace SeiRjMonitor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF Core para a entidade Consulta.
/// </summary>
public sealed class ConsultaConfiguration : IEntityTypeConfiguration<Consulta>
{
    public void Configure(EntityTypeBuilder<Consulta> builder)
    {
        builder.ToTable("consultas");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.ConsultadoEmUtc).IsRequired();
        builder.Property(c => c.Fonte).IsRequired().HasMaxLength(1000);
        builder.Property(c => c.UnidadeAtual).HasMaxLength(200);

        builder.HasIndex(c => c.ProcessoId)
            .HasDatabaseName("ix_consultas_processo");

        builder.HasIndex(c => new { c.ProcessoId, c.ConsultadoEmUtc })
            .HasDatabaseName("ix_consultas_processo_data");
    }
}
