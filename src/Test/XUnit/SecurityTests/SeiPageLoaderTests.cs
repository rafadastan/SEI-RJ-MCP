using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SeiRjMonitor.Domain;
using SeiRjMonitor.Infrastructure.Services;
using Xunit;

namespace SeiRjMonitor.Tests.SecurityTests;

public class SeiPageLoaderTests
{
    private const string UrlValida =
        "https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc";

    private static SeiPageLoader Criar(
        HttpMessageHandler handler,
        Action<SeiHttpOptions>? configurar = null)
    {
        var options = new SeiHttpOptions { TimeoutSeconds = 5, ConnectTimeoutSeconds = 3 };
        configurar?.Invoke(options);

        return new SeiPageLoader(options, NullLogger<SeiPageLoader>.Instance, handler);
    }

    [Fact]
    public async Task DeveBloquearUrlForaDoSeiRjSemFazerRequisicao()
    {
        var handler = new HandlerFalso(_ => RespostaHtml("<html><body>ok</body></html>"));
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(
            new Uri("https://evil.example.com/sei/modulos/pesquisa/md_pesq_processo_exibir.php?x"),
            CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Equal(FalhaConsultaTipo.LinkInvalido, pagina.Falha!.Tipo);
        Assert.Empty(handler.Requisicoes);
    }

    [Fact]
    public async Task DeveBaixarHtmlValido()
    {
        var handler = new HandlerFalso(_ => RespostaHtml("<html><body>conteúdo</body></html>"));
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.True(pagina.Sucesso);
        Assert.Contains("conteúdo", pagina.Html);
        Assert.Single(handler.Requisicoes);
    }

    [Fact]
    public async Task DeveSeguirRedirecionamentoParaMesmoDominio()
    {
        var destino = "https://baixada.sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc";

        var handler = new HandlerFalso(request =>
            request.RequestUri == new Uri(UrlValida)
                ? RespostaRedirecionamento(destino)
                : RespostaHtml("<html><body>final</body></html>"));

        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.True(pagina.Sucesso);
        Assert.Equal(2, handler.Requisicoes.Count);
        Assert.Equal(new Uri(destino), handler.Requisicoes[1]);
    }

    [Fact]
    public async Task DeveBloquearRedirecionamentoParaDestinoExterno()
    {
        var handler = new HandlerFalso(_ =>
            RespostaRedirecionamento("https://evil.example.com/roubo"));
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Contains("política de segurança", pagina.Falha!.Mensagem);
        Assert.Single(handler.Requisicoes);
    }

    [Fact]
    public async Task DeveBloquearRedirecionamentoParaHttp()
    {
        var handler = new HandlerFalso(_ =>
            RespostaRedirecionamento(
                "http://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc"));
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Single(handler.Requisicoes);
    }

    [Fact]
    public async Task DeveBloquearExcessoDeRedirecionamentos()
    {
        var handler = new HandlerFalso(_ => RespostaRedirecionamento(UrlValida));
        using var loader = Criar(handler, o => o.MaxRedirects = 2);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Equal(3, handler.Requisicoes.Count);
    }

    [Fact]
    public async Task DeveRetornarStatusHttpEmRespostaNaoBemSucedida()
    {
        var handler = new HandlerFalso(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound);
            return response;
        });
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Equal(404, pagina.Falha!.StatusHttp);
    }

    [Fact]
    public async Task DeveRecusarRespostaAcimaDoLimitePorContentLength()
    {
        var html = new string('a', 80 * 1024);
        var handler = new HandlerFalso(_ => RespostaHtml(html));
        using var loader = Criar(handler, o => o.MaxResponseBytes = 64 * 1024);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Contains("tamanho máximo", pagina.Falha!.Mensagem);
    }

    [Fact]
    public async Task DeveRecusarRespostaAcimaDoLimiteDuranteLeituraSemContentLength()
    {
        var handler = new HandlerFalso(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ConteudoSemTamanho(Encoding.UTF8.GetBytes(new string('a', 80 * 1024)))
        });
        using var loader = Criar(handler, o => o.MaxResponseBytes = 64 * 1024);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Contains("tamanho máximo", pagina.Falha!.Mensagem);
    }

    [Fact]
    public async Task DeveTratarTimeoutComoFalhaDeHttp()
    {
        var handler = new HandlerFalso(_ => throw new TaskCanceledException("timeout simulado"));
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.Contains("tempo limite", pagina.Falha!.Mensagem);
    }

    [Fact]
    public async Task NaoDeveExporDetalhesInternosEmFalhaDeRede()
    {
        var handler = new HandlerFalso(_ =>
            throw new HttpRequestException("connect to 10.0.0.5:5432 failed"));
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.False(pagina.Sucesso);
        Assert.DoesNotContain("10.0.0.5", pagina.Falha!.Mensagem);
        Assert.DoesNotContain("5432", pagina.Falha!.Mensagem);
    }

    [Fact]
    public async Task DevePropagarCancelamentoDoChamador()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new HandlerFalso(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return RespostaHtml("nunca");
        });
        using var loader = Criar(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loader.CarregarAsync(new Uri(UrlValida), cts.Token));
    }

    [Fact]
    public async Task DeveRespeitarCharsetIso88591()
    {
        var bytes = Encoding.Latin1.GetBytes("<html><body>não</body></html>");
        var conteudo = new ByteArrayContent(bytes);
        conteudo.Headers.ContentType =
            System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/html; charset=iso-8859-1");

        var handler = new HandlerFalso(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = conteudo
        });
        using var loader = Criar(handler);

        var pagina = await loader.CarregarAsync(new Uri(UrlValida), CancellationToken.None);

        Assert.True(pagina.Sucesso);
        Assert.Contains("não", pagina.Html);
    }

    private static HttpResponseMessage RespostaHtml(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private static HttpResponseMessage RespostaRedirecionamento(string destino)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(destino);
        return response;
    }

    private sealed class HandlerFalso : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _resposta;

        public HandlerFalso(Func<HttpRequestMessage, HttpResponseMessage> resposta)
        {
            _resposta = (request, _) => Task.FromResult(resposta(request));
        }

        public HandlerFalso(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> resposta)
        {
            _resposta = resposta;
        }

        public List<Uri> Requisicoes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requisicoes.Add(request.RequestUri!);
            return await _resposta(request, cancellationToken);
        }
    }

    private sealed class ConteudoSemTamanho : HttpContent
    {
        private readonly byte[] _bytes;

        public ConteudoSemTamanho(byte[] bytes) => _bytes = bytes;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
