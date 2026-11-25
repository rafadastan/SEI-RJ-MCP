namespace SeiRjMonitor.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para movimentações de processo.
/// </summary>
public interface IMovimentacaoRepository
{
    /// <summary>
    /// Verifica se já existe uma movimentação com a chave informada para o processo.
    /// </summary>
    Task<bool> ExisteChaveAsync(string chave, CancellationToken cancellationToken = default);
}
