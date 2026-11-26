namespace SeiRjMonitor.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para movimentações de processo.
/// </summary>
public interface IMovimentacaoRepository
{
    /// <summary>
    /// Verifica se já existe uma movimentação com a chave informada para o processo.
    /// A chave é única dentro do processo, não globalmente.
    /// </summary>
    Task<bool> ExisteChaveAsync(
        long processoId,
        string chave,
        CancellationToken cancellationToken = default);
}
