using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeiRjMonitor.Application;
using SeiRjMonitor.Domain.Interfaces;
using SeiRjMonitor.Infrastructure.Persistence;
using SeiRjMonitor.Infrastructure.Repositories;
using SeiRjMonitor.Infrastructure.Services;

namespace SeiRjMonitor.CrossCutting.Ioc;

/// <summary>
/// Registro de dependências do SEI-RJ Monitor (injeção de controle centralizada).
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adiciona os serviços de infraestrutura, aplicação e apresentação ao contêiner.
    /// A connection string é lida de <c>ConnectionStrings:SeiMonitor</c> (appsettings
    /// ou variável de ambiente <c>ConnectionStrings__SeiMonitor</c>).
    /// </summary>
    public static IServiceCollection AddSeiRjMonitor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SeiMonitor")
            ?? throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:SeiMonitor' não foi configurada. " +
                "Defina no appsettings.json ou na variável de ambiente " +
                "ConnectionStrings__SeiMonitor (ex: Host=localhost;Database=sei_monitor;Username=app;Password=...).");

        services.AddDbContext<SeiRjMonitorDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(SeiRjMonitorDbContext).Assembly.FullName)));

        // Infraestrutura
        services.AddSingleton<SeiPageLoader>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SeiRjMonitorDbContext>());
        services.AddScoped<IProcessoRepository, ProcessoRepository>();
        services.AddScoped<IConsultaRepository, ConsultaRepository>();
        services.AddScoped<IProtocoloRepository, ProtocoloRepository>();
        services.AddScoped<IMovimentacaoRepository, MovimentacaoRepository>();

        // Aplicação (escopo por operação, pois consome repositórios scoped)
        services.AddScoped<SeiConsultaService>();
        services.AddScoped<SeiMonitoramentoService>();

        return services;
    }
}
