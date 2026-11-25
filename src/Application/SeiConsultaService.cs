using SeiRjMonitor.Domain;
using SeiRjMonitor.Infrastructure.Services;

namespace SeiRjMonitor.Application;

/// <summary>
/// Orquestra a consulta pública: valida o link, baixa a página e delega o parsing.
/// </summary>
public sealed class SeiConsultaService
{
    private readonly SeiPageLoader _loader;

    public SeiConsultaService(SeiPageLoader loader) => _loader = loader;

    public async Task<ConsultaProcessoResultado> ConsultarAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!SeiLink.TryParse(url, out var link, out var erro))
            return ConsultaProcessoResultado.Erro(FalhaConsultaTipo.LinkInvalido, erro);

        var pagina = await _loader.CarregarAsync(link!.Uri, cancellationToken);

        if (!pagina.Sucesso)
        {
            return ConsultaProcessoResultado.Erro(
                pagina.Falha!.Tipo,
                pagina.Falha.Mensagem,
                pagina.Falha.StatusHttp);
        }

        return SeiPageParser.Parse(pagina.Html!, link.Uri.ToString(), DateTimeOffset.UtcNow);
    }
}
