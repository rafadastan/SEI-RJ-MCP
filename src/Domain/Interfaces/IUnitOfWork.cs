namespace SeiRjMonitor.Domain.Interfaces;

/// <summary>
/// Contrato de unidade de trabalho para confirmação de transações.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persiste todas as alterações pendentes no banco de dados.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
