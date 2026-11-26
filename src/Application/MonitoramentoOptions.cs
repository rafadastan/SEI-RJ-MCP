namespace SeiRjMonitor.Application;

/// <summary>
/// Configuração do monitoramento periódico, lida da seção <c>SeiMonitor</c>.
/// Valores são normalizados para faixas operacionais seguras.
/// </summary>
public sealed class MonitoramentoOptions
{
    public const int IntervaloMinimoMinutos = 1;
    public const int IntervaloMaximoMinutos = 24 * 60;
    public const int IntervaloPadraoMinutos = 30;

    /// <summary>
    /// Intervalo entre ciclos. Zero ou negativo desativa o monitoramento agendado.
    /// </summary>
    public int IntervalMinutes { get; set; } = IntervaloPadraoMinutos;

    /// <summary>
    /// Pausa entre processos dentro de um ciclo, para não sobrecarregar o SEI-RJ.
    /// </summary>
    public int DelayBetweenProcessesSeconds { get; set; } = 2;

    /// <summary>
    /// Tempo máximo de um ciclo completo. Protege a aplicação contra ciclos presos.
    /// </summary>
    public int CycleTimeoutSeconds { get; set; } = 30 * 60;

    /// <summary>
    /// Espera inicial antes do primeiro ciclo, permitindo que banco e rede estabilizem.
    /// </summary>
    public int StartupDelaySeconds { get; set; } = 10;

    public bool Habilitado => IntervalMinutes > 0;

    public TimeSpan Intervalo =>
        Habilitado ? TimeSpan.FromMinutes(IntervalMinutes) : TimeSpan.Zero;

    public TimeSpan PausaEntreProcessos =>
        TimeSpan.FromSeconds(Math.Clamp(DelayBetweenProcessesSeconds, 1, 60));

    public TimeSpan TempoLimiteCiclo =>
        TimeSpan.FromSeconds(Math.Clamp(CycleTimeoutSeconds, 1, 24 * 60 * 60));

    public TimeSpan EsperaInicial =>
        TimeSpan.FromSeconds(Math.Clamp(StartupDelaySeconds, 0, 300));

    /// <summary>
    /// Normaliza valores fora de faixa, mantendo o serviço previsível.
    /// Retorna os avisos de configuração para serem registrados no log.
    /// </summary>
    public IReadOnlyList<string> Normalizar()
    {
        var avisos = new List<string>();

        if (IntervalMinutes < 0)
        {
            avisos.Add("SeiMonitor:IntervalMinutes negativo; monitoramento será desativado.");
            IntervalMinutes = 0;
        }
        else if (IntervalMinutes > IntervaloMaximoMinutos)
        {
            avisos.Add(
                $"SeiMonitor:IntervalMinutes acima de {IntervaloMaximoMinutos}; será limitado.");
            IntervalMinutes = IntervaloMaximoMinutos;
        }
        else if (IntervalMinutes > 0 && IntervalMinutes < IntervaloMinimoMinutos)
        {
            avisos.Add(
                $"SeiMonitor:IntervalMinutes abaixo de {IntervaloMinimoMinutos}; será elevado.");
            IntervalMinutes = IntervaloMinimoMinutos;
        }

        if (DelayBetweenProcessesSeconds < 1)
        {
            avisos.Add("SeiMonitor:DelayBetweenProcessesSeconds abaixo de 1; será elevado.");
            DelayBetweenProcessesSeconds = 1;
        }

        if (DelayBetweenProcessesSeconds > 60)
        {
            avisos.Add("SeiMonitor:DelayBetweenProcessesSeconds acima de 60; será limitado.");
            DelayBetweenProcessesSeconds = 60;
        }

        if (CycleTimeoutSeconds < 1)
        {
            avisos.Add("SeiMonitor:CycleTimeoutSeconds abaixo de 1; será elevado.");
            CycleTimeoutSeconds = 1;
        }

        if (StartupDelaySeconds < 0)
        {
            avisos.Add("SeiMonitor:StartupDelaySeconds negativo; será zerado.");
            StartupDelaySeconds = 0;
        }

        return avisos;
    }
}
