using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SeiRjMonitor.Infrastructure.Persistence;

/// <summary>
/// Fábrica usada exclusivamente pelas ferramentas de linha de comando do EF Core
/// (<c>dotnet ef migrations</c> / <c>dotnet ef database update</c>).
/// Evita a inicialização do host de apresentação durante o design-time.
/// </summary>
public sealed class SeiRjMonitorDbContextFactory
    : IDesignTimeDbContextFactory<SeiRjMonitorDbContext>
{
    private const string ConnectionStringPadrao =
        "Host=localhost;Port=5432;Database=sei_monitor;Username=sei_monitor_app";

    public SeiRjMonitorDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__SeiMonitor");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = ConnectionStringPadrao;
        }

        var options = new DbContextOptionsBuilder<SeiRjMonitorDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(SeiRjMonitorDbContext).Assembly.FullName))
            .Options;

        return new SeiRjMonitorDbContext(options);
    }
}
