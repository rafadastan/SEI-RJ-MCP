using SeiRjMonitor.Domain;

namespace SeiRjMonitor.Application.Abstractions;

/// <summary>
/// Porta de saída para o download da página pública. A implementação concreta
/// (HTTP) fica na infraestrutura, mantendo a aplicação independente de detalhes
/// de transporte.
/// </summary>
public interface ISeiPageLoader
{
    Task<PaginaCarregada> CarregarAsync(Uri uri, CancellationToken cancellationToken);
}
