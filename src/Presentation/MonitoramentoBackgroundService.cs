using Microsoft.Extensions.Options;
using SeiRjMonitor.Application;
using SeiRjMonitor.Application.Persistence;

namespace SeiRjMonitor.Presentation;

/// <summary>
/// Resumo do último ciclo executado, exposto no endpoint de status.
/// </summary>
public sealed record ResumoCiclo(
    DateTimeOffset IniciadoEmUtc,
    DateTimeOffset ConcluidoEmUtc,
    int ProcessosConsultados,
    int Falhas);

/// <summary>
/// Consulta periodicamente todos os processos já armazenados, reutilizando o
/// link da última consulta de cada um. Cada ciclo é sequencial e nunca se
/// sobrepõe ao anterior; falhas isoladas ou em um ciclo inteiro não interrompem
/// o monitoramento. Desativado quando <c>SeiMonitor:IntervalMinutes</c> é zero
/// ou negativo.
/// </summary>
public sealed class MonitoramentoBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonitoramentoBackgroundService> _logger;
    private readonly MonitoramentoOptions _options;
    private readonly SemaphoreSlim _cicloLock = new(1, 1);

    private ResumoCiclo? _ultimoCiclo;

    public MonitoramentoBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<MonitoramentoOptions> options,
        ILogger<MonitoramentoBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;

        foreach (var aviso in _options.Normalizar())
            _logger.LogWarning("Configuração de monitoramento: {Aviso}", aviso);
    }

    public TimeSpan Intervalo => _options.Intervalo;

    public bool Habilitado => _options.Habilitado;

    public TimeSpan TempoLimiteCiclo => _options.TempoLimiteCiclo;

    public ResumoCiclo? UltimoCiclo => Volatile.Read(ref _ultimoCiclo);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Habilitado)
        {
            _logger.LogInformation(
                "Monitoramento agendado desativado (SeiMonitor:IntervalMinutes <= 0).");
            return;
        }

        _logger.LogInformation(
            "Monitoramento agendado ativo: ciclo a cada {IntervaloMinutos} minuto(s), " +
            "limite de {TempoLimiteMinutos:F1} minuto(s) por ciclo.",
            Intervalo.TotalMinutes,
            TempoLimiteCiclo.TotalMinutes);

        try
        {
            if (_options.EsperaInicial > TimeSpan.Zero)
            {
                _logger.LogInformation(
                    "Aguardando {EsperaInicial:F0}s antes do primeiro ciclo.",
                    _options.EsperaInicial.TotalSeconds);
                await Task.Delay(_options.EsperaInicial, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Monitoramento agendado encerrado antes do primeiro ciclo.");
            return;
        }

        using var timer = new PeriodicTimer(Intervalo);

        try
        {
            do
            {
                await ExecutarCicloComProtecaoAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Monitoramento agendado encerrado.");
        }
    }

    /// <summary>
    /// Executa um ciclo com timeout próprio e captura de exceções. Um ciclo com
    /// problema nunca derruba o host nem impede os ciclos seguintes.
    /// </summary>
    internal async Task ExecutarCicloComProtecaoAsync(CancellationToken stoppingToken)
    {
        if (!await _cicloLock.WaitAsync(0, stoppingToken))
        {
            _logger.LogWarning("Ciclo anterior ainda em execução; novo ciclo ignorado.");
            return;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TempoLimiteCiclo);

            try
            {
                await ExecutarCicloAsync(cts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Ciclo de monitoramento cancelado após exceder o limite de {TempoLimite:F0}s.",
                    TempoLimiteCiclo.TotalSeconds);
            }
            catch (Exception excecao)
            {
                _logger.LogError(excecao, "Falha inesperada no ciclo de monitoramento.");
            }
        }
        finally
        {
            _cicloLock.Release();
        }
    }

    internal async Task ExecutarCicloAsync(CancellationToken cancellationToken)
    {
        var iniciadoEm = DateTimeOffset.UtcNow;

        IReadOnlyList<ProcessoAgendado> processos;
        try
        {
            processos = await ComMonitoramentoAsync(
                (servico, token) => servico.ListarProcessosParaMonitorarAsync(token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception excecao)
        {
            _logger.LogError(excecao, "Falha ao listar os processos monitorados.");
            return;
        }

        if (processos.Count == 0)
        {
            _logger.LogDebug("Nenhum processo com link registrado para monitorar.");
            return;
        }

        _logger.LogInformation(
            "Iniciando ciclo de monitoramento de {Total} processo(s).", processos.Count);

        var falhas = 0;

        for (var indice = 0; indice < processos.Count; indice++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var processo = processos[indice];

            try
            {
                var consulta = await ComMonitoramentoAsync(
                    (servico, token) => servico.ConsultarAsync(processo.Url, token),
                    cancellationToken);

                if (!consulta.Resultado.Sucesso)
                {
                    falhas++;
                    var falha = consulta.Resultado.Falha!;
                    _logger.LogWarning(
                        "Falha ao consultar {Numero}: {Tipo} - {Mensagem}",
                        processo.Numero, falha.Tipo, falha.Mensagem);
                }
                else if (consulta.Persistencia is { } persistencia)
                {
                    RegistrarResultado(processo.Numero, persistencia);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception excecao)
            {
                falhas++;
                _logger.LogError(
                    excecao, "Erro inesperado ao consultar o processo {Numero}.", processo.Numero);
            }

            if (indice < processos.Count - 1)
                await Task.Delay(_options.PausaEntreProcessos, cancellationToken);
        }

        var resumo = new ResumoCiclo(
            IniciadoEmUtc: iniciadoEm,
            ConcluidoEmUtc: DateTimeOffset.UtcNow,
            ProcessosConsultados: processos.Count,
            Falhas: falhas);

        Volatile.Write(ref _ultimoCiclo, resumo);

        _logger.LogInformation(
            "Ciclo concluído em {Duracao:F1}s: {Consultados} processo(s), {Falhas} falha(s).",
            (resumo.ConcluidoEmUtc - resumo.IniciadoEmUtc).TotalSeconds,
            resumo.ProcessosConsultados,
            resumo.Falhas);
    }

    /// <summary>
    /// Executa uma operação de aplicação em um escopo próprio: o serviço de
    /// monitoramento depende de repositórios scoped (DbContext), enquanto este
    /// host é um singleton de longa duração.
    /// </summary>
    private async Task<T> ComMonitoramentoAsync<T>(
        Func<SeiMonitoramentoService, CancellationToken, Task<T>> operacao,
        CancellationToken cancellationToken)
    {
        await using var escopo = _scopeFactory.CreateAsyncScope();
        var monitoramento = escopo.ServiceProvider.GetRequiredService<SeiMonitoramentoService>();
        return await operacao(monitoramento, cancellationToken);
    }

    private void RegistrarResultado(string numero, ResultadoPersistencia persistencia)
    {
        if (persistencia.PrimeiraConsulta)
        {
            _logger.LogInformation(
                "Linha de base registrada para {Numero}: {Protocolos} protocolo(s) e " +
                "{Movimentacoes} movimentação(ões).",
                numero, persistencia.ProtocolosTotal, persistencia.MovimentacoesTotal);
            return;
        }

        if (persistencia.MovimentacoesNovas == 0)
        {
            _logger.LogInformation(
                "Nenhuma novidade em {Numero} ({Movimentacoes} movimentação(ões) conhecidas).",
                numero, persistencia.MovimentacoesTotal);
            return;
        }

        _logger.LogInformation(
            "{Numero}: {Novas} nova(s) movimentação(ões) desde a última consulta " +
            "(total {Total}).",
            numero, persistencia.MovimentacoesNovas, persistencia.MovimentacoesTotal);

        foreach (var movimentacao in persistencia.NovasMovimentacoes)
        {
            _logger.LogInformation(
                "  {DataHora:dd/MM/yyyy HH:mm} | {Unidade} | {Tipo} | {Descricao}",
                movimentacao.DataHora,
                movimentacao.Unidade,
                movimentacao.TipoEvento,
                Truncar(movimentacao.Descricao, 200));
        }
    }

    private static string Truncar(string texto, int limite) =>
        texto.Length <= limite ? texto : texto[..limite] + "…";
}
