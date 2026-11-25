namespace SeiRjMonitor.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para protocolos de processo.
/// </summary>
public interface IProtocoloRepository
{
    /// <summary>
    /// Verifica se já existe um protocolo com a chave informada para o processo.
    /// </summary>
    Task<bool> ExisteChaveAsync(string chave, CancellationToken cancellationToken = default);
}
