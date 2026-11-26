using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using SeiRjMonitor.Presentation;
using Xunit;

namespace SeiRjMonitor.Tests.SecurityTests;

public class ApiKeyAuthenticationMiddlewareTests
{
    private const string Chave = "chave-de-teste-com-mais-de-32-caracteres-aleatorios";

    [Fact]
    public void ProducaoSemChaveDeveFalharNoStartup()
    {
        var configuracao = CriarConfiguracao();

        var erro = Assert.Throws<InvalidOperationException>(() =>
            CriarMiddleware(configuracao, Environments.Production, _ => Task.CompletedTask));

        Assert.Contains("Seguranca:ApiKey", erro.Message);
    }

    [Fact]
    public async Task DesenvolvimentoSemChaveDevePermitir()
    {
        var configuracao = CriarConfiguracao();
        var chamado = false;

        var middleware = CriarMiddleware(configuracao, Environments.Development, _ =>
        {
            chamado = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CriarContexto("/mcp", null, null));

        Assert.True(chamado);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Bearer chave-errada", null)]
    [InlineData(null, "chave-errada")]
    public async Task ChaveAusenteOuInvalidaDeveRetornar401(string? autorizacao, string? apiKey)
    {
        var middleware = CriarMiddleware(
            CriarConfiguracao(("Seguranca:ApiKey", Chave)),
            Environments.Production,
            _ => Task.CompletedTask);

        var contexto = CriarContexto("/mcp", autorizacao, apiKey);
        await middleware.InvokeAsync(contexto);

        Assert.Equal(StatusCodes.Status401Unauthorized, contexto.Response.StatusCode);
        Assert.Equal("Bearer", contexto.Response.Headers.WWWAuthenticate.ToString());
    }

    [Theory]
    [InlineData("Bearer " + Chave, null)]
    [InlineData(null, Chave)]
    public async Task ChaveValidaDevePermitir(string? autorizacao, string? apiKey)
    {
        var chamado = false;
        var middleware = CriarMiddleware(
            CriarConfiguracao(("Seguranca:ApiKey", Chave)),
            Environments.Production,
            _ =>
            {
                chamado = true;
                return Task.CompletedTask;
            });

        await middleware.InvokeAsync(CriarContexto("/mcp", autorizacao, apiKey));

        Assert.True(chamado);
    }

    [Fact]
    public async Task PreflightOptionsNaoDeveExigirChave()
    {
        var chamado = false;
        var middleware = CriarMiddleware(
            CriarConfiguracao(("Seguranca:ApiKey", Chave)),
            Environments.Production,
            _ =>
            {
                chamado = true;
                return Task.CompletedTask;
            });

        var contexto = CriarContexto("/mcp", null, null);
        contexto.Request.Method = HttpMethods.Options;

        await middleware.InvokeAsync(contexto);

        Assert.True(chamado);
    }

    [Fact]
    public async Task RotasForaDoMcpNaoDevemExigirChave()
    {
        var chamado = false;
        var middleware = CriarMiddleware(
            CriarConfiguracao(("Seguranca:ApiKey", Chave)),
            Environments.Production,
            _ =>
            {
                chamado = true;
                return Task.CompletedTask;
            });

        await middleware.InvokeAsync(CriarContexto("/health/live", null, null));

        Assert.True(chamado);
    }

    private static ApiKeyAuthenticationMiddleware CriarMiddleware(
        IConfiguration configuracao,
        string ambiente,
        RequestDelegate next)
    {
        IHostEnvironment environment = new HostingEnvironment { EnvironmentName = ambiente };

        return new ApiKeyAuthenticationMiddleware(
            next,
            configuracao,
            environment,
            NullLogger<ApiKeyAuthenticationMiddleware>.Instance);
    }

    private static IConfiguration CriarConfiguracao(params (string Chave, string Valor)[] valores)
    {
        var pares = valores.ToDictionary(v => v.Chave, v => (string?)v.Valor);

        return new ConfigurationBuilder()
            .AddInMemoryCollection(pares)
            .Build();
    }

    private static DefaultHttpContext CriarContexto(
        string caminho,
        string? autorizacao,
        string? apiKey)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Path = caminho;
        contexto.Request.Method = HttpMethods.Post;
        contexto.Response.Body = new MemoryStream();

        if (autorizacao is not null)
            contexto.Request.Headers.Authorization = autorizacao;

        if (apiKey is not null)
            contexto.Request.Headers[ApiKeyAuthenticationMiddleware.CabecalhoApiKey] = apiKey;

        return contexto;
    }
}
