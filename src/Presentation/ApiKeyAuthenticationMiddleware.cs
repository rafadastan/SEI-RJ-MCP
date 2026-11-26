using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace SeiRjMonitor.Presentation;

/// <summary>
/// Autenticação por chave de API para o endpoint MCP. Aceita
/// <c>Authorization: Bearer &lt;chave&gt;</c> (padrão de clientes HTTP/MCP) ou o
/// cabeçalho <c>X-Api-Key</c>. A comparação é feita em tempo constante.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    public const string CabecalhoApiKey = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly byte[]? _chaveEsperadaHash;
    private readonly bool _exigirAutenticacao;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var chave = configuration["Seguranca:ApiKey"];
        _exigirAutenticacao = configuration.GetValue<bool?>("Seguranca:ExigirAutenticacao")
            ?? environment.IsProduction();

        if (!string.IsNullOrWhiteSpace(chave))
            _chaveEsperadaHash = SHA256.HashData(Encoding.UTF8.GetBytes(chave));

        if (_exigirAutenticacao && _chaveEsperadaHash is null)
        {
            throw new InvalidOperationException(
                "Seguranca:ApiKey é obrigatória em produção. Defina a variável de ambiente " +
                "Seguranca__ApiKey com uma chave aleatória de pelo menos 32 caracteres.");
        }

        if (_chaveEsperadaHash is null)
        {
            _logger.LogWarning(
                "Autenticação do endpoint MCP desativada. Não exponha este ambiente publicamente.");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_chaveEsperadaHash is null
            || !context.Request.Path.StartsWithSegments("/mcp")
            || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var recebida = ObterChaveRecebida(context.Request);

        if (recebida is null || !ChaveValida(recebida))
        {
            _logger.LogWarning(
                "Requisição MCP não autenticada recusada. Origem: {Origem}.",
                context.Connection.RemoteIpAddress?.ToString() ?? "desconhecida");

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { erro = "Não autorizado." });
            return;
        }

        await _next(context);
    }

    private bool ChaveValida(string recebida)
    {
        var hashRecebido = SHA256.HashData(Encoding.UTF8.GetBytes(recebida));
        return CryptographicOperations.FixedTimeEquals(hashRecebido, _chaveEsperadaHash!);
    }

    private static string? ObterChaveRecebida(HttpRequest request)
    {
        StringValues apiKey = request.Headers[CabecalhoApiKey];
        if (!StringValues.IsNullOrEmpty(apiKey))
            return apiKey.ToString();

        var autorizacao = request.Headers.Authorization.ToString();
        const string prefixoBearer = "Bearer ";

        return autorizacao.StartsWith(prefixoBearer, StringComparison.OrdinalIgnoreCase)
            ? autorizacao[prefixoBearer.Length..].Trim()
            : null;
    }
}
