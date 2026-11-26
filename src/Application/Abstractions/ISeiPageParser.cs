using SeiRjMonitor.Domain;

namespace SeiRjMonitor.Application.Abstractions;

/// <summary>
/// Porta de saída para a extração dos dados do HTML público do SEI-RJ.
/// </summary>
public interface ISeiPageParser
{
    ConsultaProcessoResultado Parse(
        string html,
        string fonte,
        DateTimeOffset consultadoEmUtc);
}
