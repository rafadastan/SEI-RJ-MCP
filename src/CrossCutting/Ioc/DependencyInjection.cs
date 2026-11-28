using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SeiRjMonitor.Application;
using SeiRjMonitor.Application.Abstractions;
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
    /// Adiciona os serviços de infraestrutura e aplicação ao contêiner.
    /// A connection string é lida de <c>ConnectionStrings:SeiMonitor</c> (appsettings
    /// ou variável de ambiente <c>ConnectionStrings__SeiMonitor</c>).
    /// </summary>
    public static IServiceCollection AddSeiRjMonitor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SeiMonitor");

        // Neon (e outros provedores gerenciados) entrega a conexão como URL
        // postgresql://; converte para o formato do Npgsql.
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = NeonDatabaseUrl.ParaConnectionString(configuration["DATABASE_URL"]);

        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = MontarConnectionStringPorVariaveis(configuration);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:SeiMonitor' não foi configurada. " +
                "Defina a variável de ambiente ConnectionStrings__SeiMonitor " +
                "(ex: Host=...;Port=5432;Database=sei_monitor;Username=...;Password=...;SSL Mode=Require) " +
                "ou DATABASE_URL com a URL do Neon (postgresql://...), " +
                "informe a senha em SEI_DB_PASSWORD/PGPASSWORD ou aponte PGPASSFILE para o pgpass.conf.");
        }

        var comandoSegundos = configuration.GetValue("Database:CommandTimeoutSeconds", 30);

        services.AddDbContext<SeiRjMonitorDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(SeiRjMonitorDbContext).Assembly.FullName);
                npgsql.CommandTimeout(Math.Clamp(comandoSegundos, 5, 120));

                // Recuperação de falhas transitórias do PostgreSQL. Sem transação
                // explícita do usuário, o retry é seguro; as constraints únicas
                // continuam protegendo contra duplicidade.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            }));

        // Configurações
        services.Configure<MonitoramentoOptions>(configuration.GetSection("SeiMonitor"));
        services.Configure<SeiHttpOptions>(configuration.GetSection("SeiMonitor:Http"));

        // Infraestrutura
        services.AddSingleton<ISeiPageLoader, SeiPageLoader>();
        services.AddSingleton<ISeiPageParser, SeiPageParser>();
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

    /// <summary>
    /// Monta a connection string a partir de variáveis de ambiente quando
    /// <c>ConnectionStrings:SeiMonitor</c> não foi definida. A senha nunca é
    /// gravada em arquivo de configuração e não aparece em logs.
    /// Ordem: senha explícita (<c>Database:PasswordEnvironmentVariable</c>,
    /// <c>SEI_DB_PASSWORD</c>, <c>SEI_MONITOR_DB_PASSWORD</c>, <c>PGPASSWORD</c>)
    /// ou, na ausência dela, o arquivo <c>PGPASSFILE</c> / pgpass padrão do sistema.
    /// Host, porta, banco e usuário usam <c>SEI_DB_*</c>/<c>PG*</c> quando presentes.
    /// </summary>
    private static string? MontarConnectionStringPorVariaveis(IConfiguration configuration)
    {
        var senha = LerVariavel(configuration["Database:PasswordEnvironmentVariable"])
            ?? LerVariavel("SEI_DB_PASSWORD")
            ?? LerVariavel("SEI_MONITOR_DB_PASSWORD")
            ?? LerVariavel("PGPASSWORD");

        var passfile = string.IsNullOrWhiteSpace(senha) ? ResolverPassfile() : null;

        if (string.IsNullOrWhiteSpace(senha) && passfile is null)
            return null;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = LerVariavel("SEI_DB_HOST") ?? LerVariavel("PGHOST") ?? "localhost",
            Database = LerVariavel("SEI_DB_NAME") ?? LerVariavel("PGDATABASE") ?? "sei_monitor",
            Username = LerVariavel("SEI_DB_USER") ?? LerVariavel("PGUSER") ?? "sei_monitor_app",
            Timeout = 10
        };

        if (!string.IsNullOrWhiteSpace(senha))
            builder.Password = senha;
        else
            builder.Passfile = passfile;

        var porta = LerVariavel("SEI_DB_PORT") ?? LerVariavel("PGPORT");
        if (int.TryParse(porta, out var portaValida) && portaValida is > 0 and <= 65535)
            builder.Port = portaValida;

        var ssl = LerVariavel("SEI_DB_SSL_MODE") ?? configuration["Database:SslMode"];
        if (Enum.TryParse<SslMode>(ssl, ignoreCase: true, out var sslMode))
            builder.SslMode = sslMode;

        return builder.ConnectionString;
    }

    /// <summary>
    /// Localiza o arquivo de senhas do PostgreSQL sem ler seu conteúdo:
    /// <c>PGPASSFILE</c>, o padrão do Windows e o padrão Unix.
    /// </summary>
    private static string? ResolverPassfile()
    {
        var configurado = LerVariavel("PGPASSFILE");
        if (!string.IsNullOrWhiteSpace(configurado) && File.Exists(configurado))
            return configurado;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var padraoWindows = Path.Combine(appData, "postgresql", "pgpass.conf");
        if (File.Exists(padraoWindows))
            return padraoWindows;

        var padraoUnix = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pgpass");
        return File.Exists(padraoUnix) ? padraoUnix : null;
    }

    private static string? LerVariavel(string? nome) =>
        string.IsNullOrWhiteSpace(nome) ? null : Environment.GetEnvironmentVariable(nome);
}
