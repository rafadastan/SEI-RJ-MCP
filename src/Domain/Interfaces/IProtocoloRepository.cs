namespace SeiRjMonitor.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para protocolos de processo.
/// </summary>
public interface IProtocoloRepository
{
    /// <summary>
    /// Verifica se já existe um protocolo com a chave informada para o processo.
    /// A chave é única dentro do processo, não globalmente.
    /// </summary>
    Task<bool> ExisteChaveAsync(
        long processoId,
        string chave,
        CancellationToken cancellationToken = default);
}
