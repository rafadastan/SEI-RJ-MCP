using ModelContextProtocol.Server;
using SeiRjMonitor.CrossCutting.Ioc;
using SeiRjMonitor.Presentation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSeiRjMonitor(builder.Configuration);

// Serviços de apresentação (registrados aqui para evitar dependência circular
// entre CrossCutting e Presentation).
builder.Services.AddSingleton<MonitoramentoBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitoramentoBackgroundService>());
builder.Services.AddSingleton<SeiTools>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/", (
    IConfiguration configuration,
    MonitoramentoBackgroundService monitoramento) => Results.Ok(new
{
    name = "SEI-RJ Monitor",
    status = "running",
    mcp = "/mcp",
    scope = "Somente informações públicas",
    database = "PostgreSQL (ConnectionStrings:SeiMonitor)",
    connectionStringConfigurada = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("SeiMonitor")),
    monitoramento = new
    {
        habilitado = monitoramento.Habilitado,
        intervaloMinutos = monitoramento.Intervalo.TotalMinutes,
        ultimoCiclo = monitoramento.UltimoCiclo
    }
}));

app.MapMcp("/mcp");

app.Run();
