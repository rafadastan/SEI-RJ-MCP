using System.Net;
using System.Text;

namespace SeiRjMonitor.Infrastructure.Services;

using PaginaCarregada = SeiRjMonitor.Domain.PaginaCarregada;

/// <summary>
/// Baixa o HTML da consulta pública respeitando o charset informado pelo SEI-RJ.
/// </summary>
public sealed class SeiPageLoader : IDisposable
{
    private readonly HttpClient _http;

    public SeiPageLoader()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(40)
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/120.0.0.0 Safari/537.36 SEI-RJ-Monitor/0.3");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9");
    }

    public async Task<PaginaCarregada> CarregarAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return PaginaCarregada.Erro(
                    Domain.FalhaConsultaTipo.FalhaHttp,
                    "O SEI-RJ não retornou uma resposta de sucesso.",
                    (int)response.StatusCode);
            }

            var html = await ReadHtmlAsync(response, cancellationToken);

            return string.IsNullOrWhiteSpace(html)
                ? PaginaCarregada.Erro(Domain.FalhaConsultaTipo.PaginaVazia, "O SEI-RJ retornou uma página vazia.")
                : PaginaCarregada.Ok(html);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PaginaCarregada.Erro(
                Domain.FalhaConsultaTipo.FalhaHttp,
                "A consulta ao SEI-RJ excedeu o tempo limite.");
        }
        catch (HttpRequestException ex)
        {
            return PaginaCarregada.Erro(
                Domain.FalhaConsultaTipo.FalhaHttp,
                $"Falha de rede ao consultar o SEI-RJ: {ex.Message}");
        }
    }

    private static async Task<string> ReadHtmlAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // O SEI-RJ responde em iso-8859-1. Decodificar explicitamente pelos bytes
        // evita mojibake quando o header vem incompleto.
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        return ResolveEncoding(charset).GetString(bytes);
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            if (charset.Equals("iso-8859-1", StringComparison.OrdinalIgnoreCase)
                || charset.Equals("latin1", StringComparison.OrdinalIgnoreCase))
                return Encoding.Latin1;

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // Charset desconhecido no runtime: segue para UTF-8.
            }
        }

        return Encoding.UTF8;
    }

    public void Dispose() => _http.Dispose();
}
