using System.ComponentModel;
using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SeiRjMonitor.Application;
using SeiRjMonitor.Application.Persistence;
using SeiRjMonitor.Domain;

[McpServerToolType]
public sealed class SeiTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeiTools> _logger;

    public SeiTools(IServiceScopeFactory scopeFactory, ILogger<SeiTools> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Executa uma operação de aplicação em um escopo próprio, garantindo um
    /// DbContext isolado por chamada (as ferramentas MCP são singletons).
    /// </summary>
    private async Task<T> ComMonitoramentoAsync<T>(
        Func<SeiMonitoramentoService, CancellationToken, Task<T>> operacao,
        CancellationToken cancellationToken)
    {
        await using var escopo = _scopeFactory.CreateAsyncScope();
        var monitoramento = escopo.ServiceProvider.GetRequiredService<SeiMonitoramentoService>();
        return await operacao(monitoramento, cancellationToken);
    }

    [McpServerTool, Description(
        "Consulta uma página de pesquisa processual pública do SEI-RJ a partir do link completo, " +
        "registra o resultado no banco local e informa o que mudou desde a consulta anterior. " +
        "Retorna metadados do processo, protocolos públicos listados, histórico de tramitação visível " +
        "e a lista de movimentações novas detectadas. " +
        "Não acessa documentos restritos, não resolve CAPTCHA e não contorna autenticação.")]
    public Task<string> ConsultarProcesso(
        [Description("URL completa da página pública de consulta do processo no domínio sei.rj.gov.br.")] string url,
        CancellationToken cancellationToken = default) =>
        ComTratamentoAsync(nameof(ConsultarProcesso), async () =>
        {
            var consulta = await ComMonitoramentoAsync(
                (servico, token) => servico.ConsultarAsync(url, token), cancellationToken);

            if (!consulta.Resultado.Sucesso)
                return SerializarFalha(consulta.Resultado.Falha!);

            return JsonSerializer.Serialize(
                MontarSaida(consulta.Resultado.Processo!, consulta.Persistencia),
                JsonOptions);
        }, cancellationToken);

    [McpServerTool, Description(
        "Valida se um link pertence à consulta pública do SEI-RJ e identifica o formato da URL. " +
        "Use quando o usuário fornecer um link antes de solicitar a extração.")]
    public string ValidarLinkProcesso(
        [Description("URL da consulta pública do SEI-RJ.")] string url)
    {
        if (!SeiLink.TryParse(url, out var link, out var erro))
            return JsonSerializer.Serialize(new { valido = false, erro }, JsonOptions);

        return JsonSerializer.Serialize(new
        {
            valido = true,
            host = link!.Uri.Host,
            caminho = link.Uri.AbsolutePath,
            possuiParametroDeConsulta = link.PossuiParametroDeConsulta,
            formatoParametro = link.FormatoParametro,
            observacao = "O link foi validado estruturalmente. A disponibilidade dos dados só pode ser confirmada durante a consulta."
        }, JsonOptions);
    }

    [McpServerTool, Description(
        "Recupera o histórico local de um processo já consultado: consultas registradas, " +
        "movimentações detectadas e carimbos de primeira detecção. " +
        "Não faz nova consulta ao SEI-RJ.")]
    public Task<string> HistoricoProcesso(
        [Description("Número do processo, como SEI-260005/005504/2024.")] string numero,
        CancellationToken cancellationToken = default) =>
        ComTratamentoAsync(nameof(HistoricoProcesso), async () =>
        {
            var historico = await ComMonitoramentoAsync(
                (servico, token) => servico.ObterHistoricoAsync(numero, token), cancellationToken);

            if (historico is null)
            {
                return JsonSerializer.Serialize(new
                {
                    encontrado = false,
                    numero,
                    observacao = "Nenhuma consulta registrada para este processo. Use consultar_processo com o link público para criar a linha de base."
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new
            {
                encontrado = true,
                historico.Numero,
                historico.Tipo,
                historico.DataGeracao,
                historico.Interessados,
                historico.AcessoRestrito,
                historico.PrimeiraConsultaUtc,
                historico.UltimaConsultaUtc,
                historico.TotalConsultas,
                quantidadeMovimentacoes = historico.Movimentacoes.Count,
                consultas = historico.Consultas,
                movimentacoes = historico.Movimentacoes
            }, JsonOptions);
        }, cancellationToken);

    [McpServerTool, Description(
        "Lista os processos já consultados e registrados no banco local, " +
        "do mais recente para o mais antigo, com contagem de consultas e movimentações.")]
    public Task<string> ListarProcessosMonitorados(
        CancellationToken cancellationToken = default) =>
        ComTratamentoAsync(nameof(ListarProcessosMonitorados), async () =>
        {
            var processos = await ComMonitoramentoAsync(
                (servico, token) => servico.ListarProcessosAsync(token), cancellationToken);

            return JsonSerializer.Serialize(new
            {
                quantidade = processos.Count,
                processos
            }, JsonOptions);
        }, cancellationToken);

    /// <summary>
    /// Converte falhas de banco/rede em uma resposta JSON estável, em vez de deixar
    /// a exceção escapar da ferramenta MCP. Erros de programação continuam propagando.
    /// </summary>
    private async Task<string> ComTratamentoAsync(
        string ferramenta,
        Func<Task<string>> operacao,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operacao();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao) when (excecao is DbException or DbUpdateException or TimeoutException)
        {
            _logger.LogError(excecao, "Falha de infraestrutura na ferramenta MCP {Ferramenta}.", ferramenta);

            return JsonSerializer.Serialize(new
            {
                erro = "Não foi possível acessar o banco de dados local. " +
                       "Verifique a disponibilidade do PostgreSQL e a configuração da conexão.",
                tipoFalha = "FalhaInfraestrutura"
            }, JsonOptions);
        }
    }

    private static object MontarSaida(
        ProcessoConsultado processo,
        ResultadoPersistencia? persistencia) => new
    {
        fonte = processo.Fonte,
        consultadoEmUtc = processo.ConsultadoEmUtc,
        processo = processo.Numero,
        tipo = processo.Tipo,
        dataGeracao = processo.DataGeracao,
        interessados = processo.Interessados,
        acessoRestrito = processo.AcessoRestrito,
        avisoAcesso = processo.AcessoRestrito
            ? "O processo possui restrição de acesso. Apenas os dados públicos exibidos na consulta foram extraídos; o conteúdo restrito não é acessado."
            : null,
        protocolosDeclarados = processo.ProtocolosDeclarados,
        quantidadeProtocolos = processo.QuantidadeProtocolos,
        protocolosComRestricao = processo.ProtocolosComRestricao,
        protocolos = processo.Protocolos,
        andamentosDeclarados = processo.AndamentosDeclarados,
        quantidadeMovimentacoes = processo.QuantidadeMovimentacoes,
        unidadeAtual = processo.UnidadeAtual,
        ultimaMovimentacaoEm = processo.UltimaMovimentacaoEm,
        movimentacoes = processo.Movimentacoes,
        monitoramento = MontarMonitoramento(persistencia),
        observacao = "Somente informações efetivamente exibidas na consulta pública foram extraídas. Acesso restrito não é contornado."
    };

    private static object? MontarMonitoramento(ResultadoPersistencia? persistencia)
    {
        if (persistencia is null)
            return null;

        return new
        {
            persistencia.ProcessoId,
            persistencia.ConsultaId,
            persistencia.PrimeiraConsulta,
            persistencia.ProtocolosNovos,
            persistencia.ProtocolosTotal,
            persistencia.MovimentacoesNovas,
            persistencia.MovimentacoesTotal,
            persistencia.NovasMovimentacoes,
            aviso = persistencia.PrimeiraConsulta
                ? "Primeira consulta registrada como linha de base: todas as movimentações foram armazenadas e nenhuma é marcada como nova."
                : persistencia.MovimentacoesNovas == 0
                    ? "Nenhuma movimentação nova desde a última consulta registrada."
                    : $"{persistencia.MovimentacoesNovas} movimentação(ões) nova(s) desde a última consulta registrada."
        };
    }

    private static string SerializarFalha(FalhaConsulta falha)
    {
        var payload = new Dictionary<string, object?>
        {
            ["erro"] = falha.Mensagem,
            ["tipoFalha"] = falha.Tipo.ToString()
        };

        switch (falha.Tipo)
        {
            case FalhaConsultaTipo.ProcessoNaoEncontrado:
                payload["processoNaoEncontrado"] = true;
                break;

            case FalhaConsultaTipo.RequerInteracaoHumana:
            case FalhaConsultaTipo.LayoutDesconhecido:
                payload["requerInteracaoHumana"] = true;
                break;
        }

        if (falha.StatusHttp is { } statusHttp)
            payload["statusHttp"] = statusHttp;

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
