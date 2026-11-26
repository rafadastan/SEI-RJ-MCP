namespace SeiRjMonitor.Infrastructure.Services;

/// <summary>
/// Limites de rede para o download da página pública do SEI-RJ.
/// </summary>
public sealed class SeiHttpOptions
{
    /// <summary>Tempo máximo total de uma requisição, incluindo redirecionamentos.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Tempo máximo para estabelecer a conexão TCP.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>Tamanho máximo do HTML aceito (5 MB por padrão).</summary>
    public long MaxResponseBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Quantidade máxima de redirecionamentos seguidos.</summary>
    public int MaxRedirects { get; set; } = 5;

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 5, 120));

    public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(Math.Clamp(ConnectTimeoutSeconds, 3, 60));

    public long LimiteBytes => Math.Clamp(MaxResponseBytes, 64 * 1024, 20 * 1024 * 1024);

    public int LimiteRedirecionamentos => Math.Clamp(MaxRedirects, 0, 10);
}
