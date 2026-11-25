using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeiRjMonitor.Domain.Aggregates;

namespace SeiRjMonitor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF Core para a entidade ProtocoloItem.
/// </summary>
public sealed class ProtocoloConfiguration : IEntityTypeConfiguration<ProtocoloItem>
{
    public void Configure(EntityTypeBuilder<ProtocoloItem> builder)
    {
        builder.ToTable("protocolos");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.Chave)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(p => p.Numero).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Natureza).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Tipo).HasMaxLength(100);
        builder.Property(p => p.Data).HasMaxLength(50);
        builder.Property(p => p.DataInclusao).HasMaxLength(50);
        builder.Property(p => p.Unidade).HasMaxLength(200);

        builder.Property(p => p.AcessoRestrito).IsRequired();
        builder.Property(p => p.PrimeiraDeteccaoUtc).IsRequired();
        builder.Property(p => p.UltimaDeteccaoUtc).IsRequired();

        builder.HasIndex(p => p.ProcessoId)
            .HasDatabaseName("ix_protocolos_processo");

        builder.HasIndex(p => new { p.ProcessoId, p.Chave })
            .IsUnique()
            .HasDatabaseName("ux_protocolos_processo_chave");
    }
}
