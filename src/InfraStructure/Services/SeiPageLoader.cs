using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeiRjMonitor.Application.Abstractions;
using SeiRjMonitor.Infrastructure.Services;

namespace SeiRjMonitor.Infrastructure.Services;

using PaginaCarregada = SeiRjMonitor.Domain.PaginaCarregada;
using SeiLink = SeiRjMonitor.Domain.SeiLink;

/// <summary>
/// Baixa o HTML da consulta pública respeitando o charset informado pelo SEI-RJ.
/// Proteções anti-SSRF: allowlist de host/caminho, redirecionamentos manuais
/// revalidados, resolução DNS validada e endereço IP final verificado no momento
/// da conexão (impede DNS rebinding), além de limite de tamanho e timeout.
/// </summary>
public sealed class SeiPageLoader : ISeiPageLoader, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<SeiPageLoader> _logger;
    private readonly SeiHttpOptions _options;
    private bool _descartado;

    public SeiPageLoader(
        IOptions<SeiHttpOptions> options,
        ILogger<SeiPageLoader> logger)
        : this(options.Value, logger, handler: null)
    {
    }

    /// <summary>
    /// Construtor usado em testes para injetar um handler HTTP controlado.
    /// </summary>
    public SeiPageLoader(
        SeiHttpOptions options,
        ILogger<SeiPageLoader> logger,
        HttpMessageHandler? handler)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var socketsHandler = handler as SocketsHttpHandler
            ?? (handler is null ? CriarHandlerSeguro(options) : null);

        _http = new HttpClient(handler ?? socketsHandler!, disposeHandler: true)
        {
            Timeout = options.Timeout
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/120.0.0.0 Safari/537.36 SEI-RJ-Monitor/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9");
    }

    public async Task<PaginaCarregada> CarregarAsync(Uri uri, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);

        if (!SeiLink.TryParse(uri.ToString(), out _, out _))
        {
            return PaginaCarregada.Erro(
                Domain.FalhaConsultaTipo.LinkInvalido,
                "O link informado não pertence à consulta pública oficial do SEI-RJ.");
        }

        try
        {
            // Timeout total da operação, incluindo leitura do corpo. O
            // HttpClient.Timeout sozinho cobre apenas o recebimento dos headers
            // quando se usa ResponseHeadersRead.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.Timeout);
            var requestToken = timeoutCts.Token;

            var currentUri = uri;

            for (var redirectCount = 0; ; redirectCount++)
            {
                using var response = await _http.GetAsync(
                    currentUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken);

                if ((int)response.StatusCode is >= 300 and <= 399)
                {
                    var location = response.Headers.Location;

                    if (location is null || redirectCount >= _options.LimiteRedirecionamentos)
                    {
                        return PaginaCarregada.Erro(
                            Domain.FalhaConsultaTipo.FalhaHttp,
                            "Muitos redirecionamentos ou redirecionamento inválido ao consultar o SEI-RJ.");
                    }

                    var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);

                    // Revalida host, esquema, porta e caminho antes de seguir.
                    if (!SeiLink.TryParse(nextUri.ToString(), out _, out _))
                    {
                        _logger.LogWarning(
                            "Redirecionamento bloqueado por política de segurança (destino fora do SEI-RJ).");
                        return PaginaCarregada.Erro(
                            Domain.FalhaConsultaTipo.FalhaHttp,
                            "Redirecionamento bloqueado por política de segurança.");
                    }

                    currentUri = nextUri;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return PaginaCarregada.Erro(
                        Domain.FalhaConsultaTipo.FalhaHttp,
                        "O SEI-RJ não retornou uma resposta de sucesso.",
                        (int)response.StatusCode);
                }

                if (response.Content.Headers.ContentLength is { } tamanho
                    && tamanho > _options.LimiteBytes)
                {
                    _logger.LogWarning(
                        "Resposta do SEI-RJ acima do limite configurado ({Tamanho} bytes).", tamanho);
                    return PaginaCarregada.Erro(
                        Domain.FalhaConsultaTipo.FalhaHttp,
                        "A resposta do SEI-RJ excedeu o tamanho máximo permitido.");
                }

                var html = await ReadHtmlAsync(response, requestToken);

                return string.IsNullOrWhiteSpace(html)
                    ? PaginaCarregada.Erro(
                        Domain.FalhaConsultaTipo.PaginaVazia,
                        "O SEI-RJ retornou uma página vazia.")
                    : PaginaCarregada.Ok(html);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelamento solicitado pelo chamador (shutdown ou timeout do ciclo).
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout ao consultar o SEI-RJ.");
            return PaginaCarregada.Erro(
                Domain.FalhaConsultaTipo.FalhaHttp,
                "A consulta ao SEI-RJ excedeu o tempo limite.");
        }
        catch (RespostaMuitoGrandeException)
        {
            return PaginaCarregada.Erro(
                Domain.FalhaConsultaTipo.FalhaHttp,
                "A resposta do SEI-RJ excedeu o tamanho máximo permitido.");
        }
        catch (HttpRequestException excecao)
        {
            // Detalhes ficam apenas no log do servidor; o cliente recebe mensagem genérica.
            _logger.LogWarning(excecao, "Falha de rede ao consultar o SEI-RJ.");
            return PaginaCarregada.Erro(
                Domain.FalhaConsultaTipo.FalhaHttp,
                "Não foi possível conectar ao SEI-RJ no momento.");
        }
        catch (IOException excecao)
        {
            _logger.LogWarning(excecao, "Falha de I/O ao ler a resposta do SEI-RJ.");
            return PaginaCarregada.Erro(
                Domain.FalhaConsultaTipo.FalhaHttp,
                "Falha ao ler a resposta do SEI-RJ.");
        }
    }

    private static SocketsHttpHandler CriarHandlerSeguro(SeiHttpOptions options) =>
        new()
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = options.ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 4,
            ConnectCallback = async (contexto, cancellationToken) =>
            {
                var enderecos = await SegurancaRede.ResolverDestinoSeguroAsync(
                    contexto.DnsEndPoint.Host,
                    cancellationToken);

                Exception? ultimaFalha = null;

                foreach (var endereco in enderecos)
                {
                    var socket = new Socket(endereco.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true
                    };

                    try
                    {
                        await socket.ConnectAsync(
                            new IPEndPoint(endereco, contexto.DnsEndPoint.Port),
                            cancellationToken);

                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception excecao) when (excecao is not OperationCanceledException)
                    {
                        ultimaFalha = excecao;
                        socket.Dispose();
                    }
                }

                throw new HttpRequestException(
                    "Não foi possível conectar a nenhum endereço público do destino.",
                    ultimaFalha);
            }
        };

    private async Task<string> ReadHtmlAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memoria = new MemoryStream();

        var buffer = new byte[16 * 1024];
        long total = 0;

        while (true)
        {
            var lidos = await stream.ReadAsync(buffer, cancellationToken);
            if (lidos == 0)
                break;

            total += lidos;
            if (total > _options.LimiteBytes)
            {
                _logger.LogWarning(
                    "Resposta do SEI-RJ excedeu o limite de {Limite} bytes durante a leitura.",
                    _options.LimiteBytes);
                throw new RespostaMuitoGrandeException();
            }

            memoria.Write(buffer, 0, lidos);
        }

        var bytes = memoria.ToArray();

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
            catch (Exception excecao) when (excecao is ArgumentException or NotSupportedException)
            {
                // Charset desconhecido no runtime: segue para UTF-8.
            }
        }

        return Encoding.UTF8;
    }

    public void Dispose()
    {
        if (_descartado)
            return;

        _descartado = true;
        _http.Dispose();
    }

    private sealed class RespostaMuitoGrandeException : Exception;
}
