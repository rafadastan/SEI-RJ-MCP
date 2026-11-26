using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SeiRjMonitor.CrossCutting.Ioc;
using SeiRjMonitor.Presentation;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize =
        builder.Configuration.GetValue<long?>("Kestrel:Limits:MaxRequestBodySize") ?? 1024 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddSeiRjMonitor(builder.Configuration);

// Serviços de apresentação (registrados aqui para evitar dependência circular
// entre CrossCutting e Presentation).
builder.Services.AddSingleton<MonitoramentoBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitoramentoBackgroundService>());
builder.Services.AddSingleton<SeiTools>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgresql", tags: new[] { "ready" });

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var origensPermitidas = builder.Configuration
    .GetSection("Seguranca:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

if (origensPermitidas.Length > 0)
{
    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(origensPermitidas)
            .WithMethods("GET", "POST", "DELETE", "OPTIONS")
            .WithHeaders(
                "Authorization",
                "Content-Type",
                "Accept",
                "Mcp-Session-Id",
                ApiKeyAuthenticationMiddleware.CabecalhoApiKey)
            .WithExposedHeaders("Mcp-Session-Id")));
}

ValidarConfiguracaoDeSeguranca(builder.Configuration, builder.Environment);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new
        {
            title = "Erro interno do servidor.",
            status = StatusCodes.Status500InternalServerError
        });
    }));

    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

if (origensPermitidas.Length > 0)
    app.UseCors();

app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

if (!app.Environment.IsDevelopment())
{
    var allowedHosts = builder.Configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Contains('*'))
    {
        app.Logger.LogWarning(
            "AllowedHosts está aberto ('*'). Em produção, defina AllowedHosts com o domínio real " +
            "para reduzir risco de host header injection.");
    }
}

app.MapGet("/", (MonitoramentoBackgroundService monitoramento) => Results.Ok(new
{
    name = "SEI-RJ Monitor",
    status = "running",
    mcp = "/mcp",
    health = new { live = "/health/live", ready = "/health/ready" },
    scope = "Somente informações públicas",
    monitoramento = new
    {
        habilitado = monitoramento.Habilitado,
        intervaloMinutos = monitoramento.Intervalo.TotalMinutes,
        tempoLimiteCicloMinutos = monitoramento.TempoLimiteCiclo.TotalMinutes,
        ultimoCiclo = monitoramento.UltimoCiclo
    }
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = EscreverHealth
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registro => registro.Tags.Contains("ready"),
    ResponseWriter = EscreverHealth
});

app.MapMcp("/mcp");

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation(
    "SEI-RJ Monitor iniciado em {Ambiente}. MCP: /mcp | Health: /health/live e /health/ready | " +
    "Autenticação MCP: {Autenticacao}.",
    app.Environment.EnvironmentName,
    string.IsNullOrWhiteSpace(builder.Configuration["Seguranca:ApiKey"]) ? "desativada" : "ativa");

app.Run();

static void ValidarConfiguracaoDeSeguranca(IConfiguration configuration, IHostEnvironment environment)
{
    var chave = configuration["Seguranca:ApiKey"];
    var exigir = configuration.GetValue<bool?>("Seguranca:ExigirAutenticacao")
        ?? environment.IsProduction();

    if (exigir && string.IsNullOrWhiteSpace(chave))
    {
        throw new InvalidOperationException(
            "Seguranca:ApiKey é obrigatória em produção. Defina Seguranca__ApiKey com uma " +
            "chave aleatória de pelo menos 32 caracteres.");
    }

    if (!string.IsNullOrWhiteSpace(chave) && chave.Length < 32)
    {
        throw new InvalidOperationException(
            "Seguranca:ApiKey deve ter pelo menos 32 caracteres.");
    }

    if (string.Equals(chave, "change_me", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Seguranca:ApiKey não pode usar o valor de exemplo 'change_me'.");
    }
}

static Task EscreverHealth(HttpContext context, HealthReport relatorio)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    return context.Response.WriteAsJsonAsync(new
    {
        status = relatorio.Status.ToString(),
        verificacaoEmUtc = DateTimeOffset.UtcNow
    });
}
