using SeiRjMonitor.Domain.Aggregates;

namespace SeiRjMonitor.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para consultas de processo.
/// </summary>
public interface IConsultaRepository
{
    /// <summary>
    /// Adiciona uma nova consulta.
    /// </summary>
    Task AdicionarAsync(Consulta consulta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera todas as consultas de um processo, ordenadas por data.
    /// </summary>
    Task<IReadOnlyList<Consulta>> ObterPorProcessoAsync(
        string numero,
        CancellationToken cancellationToken = default);
}
