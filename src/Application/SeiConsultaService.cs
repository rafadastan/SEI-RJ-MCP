using SeiRjMonitor.Application.Abstractions;
using SeiRjMonitor.Domain;

namespace SeiRjMonitor.Application;

/// <summary>
/// Orquestra a consulta pública: valida o link, baixa a página e delega o parsing.
/// </summary>
public sealed class SeiConsultaService
{
    private readonly ISeiPageLoader _loader;
    private readonly ISeiPageParser _parser;

    public SeiConsultaService(ISeiPageLoader loader, ISeiPageParser parser)
    {
        _loader = loader;
        _parser = parser;
    }

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

        return _parser.Parse(pagina.Html!, link.Uri.ToString(), DateTimeOffset.UtcNow);
    }
}
