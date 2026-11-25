namespace SeiRjMonitor.Domain;

/// <summary>
/// Resultado do download da página pública: ou o HTML já decodificado, ou a falha.
/// </summary>
public sealed record PaginaCarregada(string? Html, FalhaConsulta? Falha)
{
    public bool Sucesso => Html is not null;

    public static PaginaCarregada Ok(string html) => new(html, null);

    public static PaginaCarregada Erro(
        FalhaConsultaTipo tipo,
        string mensagem,
        int? statusHttp = null) =>
        new(null, new FalhaConsulta(tipo, mensagem, statusHttp));
}
