using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SeiRjMonitor.Domain.Aggregates;

namespace SeiRjMonitor.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF Core para a entidade MovimentacaoItem.
/// </summary>
public sealed class MovimentacaoConfiguration : IEntityTypeConfiguration<MovimentacaoItem>
{
    public void Configure(EntityTypeBuilder<MovimentacaoItem> builder)
    {
        builder.ToTable("movimentacoes");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        builder.Property(m => m.Chave)
            .IsRequired()
            .HasMaxLength(64);

        // O SEI-RJ exibe data/hora local sem fuso (DateTimeKind.Unspecified).
        // "timestamp with time zone" faria o Npgsql rejeitar a escrita.
        builder.Property(m => m.DataHora)
            .IsRequired()
            .HasColumnType("timestamp without time zone");
        builder.Property(m => m.Unidade).IsRequired().HasMaxLength(200);
        builder.Property(m => m.UnidadeRelacionada).HasMaxLength(200);
        builder.Property(m => m.TipoEvento).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Descricao).IsRequired().HasMaxLength(2000);

        builder.Property(m => m.DetectadaEmUtc).IsRequired();

        builder.HasIndex(m => m.ProcessoId)
            .HasDatabaseName("ix_movimentacoes_processo");

        builder.HasIndex(m => new { m.ProcessoId, m.DataHora })
            .HasDatabaseName("ix_movimentacoes_processo_data");

        builder.HasIndex(m => new { m.ProcessoId, m.Chave })
            .IsUnique()
            .HasDatabaseName("ux_movimentacoes_processo_chave");
    }
}
