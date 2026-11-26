using System.Security.Cryptography;
using System.Text;
using SeiRjMonitor.Application.Persistence;
using SeiRjMonitor.Domain;
using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Domain.Interfaces;

namespace SeiRjMonitor.Application;

/// <summary>
/// Resultado de uma consulta já registrada no banco local.
/// </summary>
public sealed record ConsultaMonitorada(
    ConsultaProcessoResultado Resultado,
    ResultadoPersistencia? Persistencia);

/// <summary>
/// Compõe a consulta pública com o armazenamento PostgreSQL, permitindo acompanhar
/// o que mudou entre duas consultas ao mesmo processo.
/// </summary>
public sealed class SeiMonitoramentoService
{
    private readonly SeiConsultaService _consulta;
    private readonly IProcessoRepository _processos;
    private readonly IConsultaRepository _consultas;
    private readonly IProtocoloRepository _protocolos;
    private readonly IMovimentacaoRepository _movimentacoes;
    private readonly IUnitOfWork _unitOfWork;

    public SeiMonitoramentoService(
        SeiConsultaService consulta,
        IProcessoRepository processos,
        IConsultaRepository consultas,
        IProtocoloRepository protocolos,
        IMovimentacaoRepository movimentacoes,
        IUnitOfWork unitOfWork)
    {
        _consulta = consulta;
        _processos = processos;
        _consultas = consultas;
        _protocolos = protocolos;
        _movimentacoes = movimentacoes;
        _unitOfWork = unitOfWork;
    }

    public async Task<ConsultaMonitorada> ConsultarAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _consulta.ConsultarAsync(url, cancellationToken);

        if (!resultado.Sucesso)
            return new ConsultaMonitorada(resultado, null);

        var processoConsultado = resultado.Processo!;
        var numero = SeiTextos.NuloSeVazio(processoConsultado.Numero);
        if (numero is null)
            return new ConsultaMonitorada(resultado, null);

        var existente = await _processos.ObterPorNumeroAsync(numero, cancellationToken);
        var primeiraConsulta = existente is null;

        var processo = existente ?? new Processo(numero, processoConsultado.ConsultadoEmUtc);

        processo.AtualizarDados(
            processoConsultado.Tipo,
            processoConsultado.DataGeracao,
            processoConsultado.Interessados,
            processoConsultado.AcessoRestrito,
            processoConsultado.UnidadeAtual,
            processoConsultado.ConsultadoEmUtc);

        if (primeiraConsulta)
            await _processos.AdicionarAsync(processo, cancellationToken);
        else
            await _processos.AtualizarAsync(processo, cancellationToken);

        var consultaEntidade = processo.AdicionarConsulta(
            processoConsultado.ConsultadoEmUtc,
            processoConsultado.Fonte,
            processoConsultado.ProtocolosDeclarados,
            processoConsultado.AndamentosDeclarados,
            processoConsultado.QuantidadeProtocolos,
            processoConsultado.QuantidadeMovimentacoes,
            processoConsultado.UnidadeAtual);

        await _consultas.AdicionarAsync(consultaEntidade, cancellationToken);

        var protocolosNovos = 0;
        foreach (var protocolo in processoConsultado.Protocolos)
        {
            var chave = CalcularChaveProtocolo(protocolo);

            if (!processo.PossuiProtocoloComChave(chave) &&
                !await _protocolos.ExisteChaveAsync(processo.Id, chave, cancellationToken))
            {
                var protocoloItem = new ProtocoloItem(
                    processo.Id,
                    chave,
                    protocolo,
                    processoConsultado.ConsultadoEmUtc);
                processo.AdicionarProtocolo(protocoloItem);

                if (!primeiraConsulta)
                    protocolosNovos++;
            }
            else
            {
                // Atualiza a última detecção do protocolo existente
                var item = processo.Protocolos.FirstOrDefault(p => p.Chave == chave);
                if (item is not null)
                    item.AtualizarUltimaDeteccao(processoConsultado.ConsultadoEmUtc);
            }
        }

        var novasMovimentacoes = new List<Domain.Movimentacao>();
        foreach (var movimentacao in processoConsultado.Movimentacoes)
        {
            var chave = CalcularChaveMovimentacao(movimentacao);

            if (!processo.PossuiMovimentacaoComChave(chave) &&
                !await _movimentacoes.ExisteChaveAsync(processo.Id, chave, cancellationToken))
            {
                var movimentacaoItem = new MovimentacaoItem(
                    processo.Id,
                    consultaEntidade.Id,
                    chave,
                    movimentacao,
                    processoConsultado.ConsultadoEmUtc);
                processo.AdicionarMovimentacao(movimentacaoItem);

                if (!primeiraConsulta)
                    novasMovimentacoes.Add(movimentacao);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ConsultaMonitorada(
            resultado,
            new ResultadoPersistencia(
                ProcessoId: processo.Id,
                ConsultaId: consultaEntidade.Id,
                PrimeiraConsulta: primeiraConsulta,
                ProtocolosNovos: protocolosNovos,
                ProtocolosTotal: processoConsultado.QuantidadeProtocolos,
                MovimentacoesNovas: novasMovimentacoes.Count,
                MovimentacoesTotal: processoConsultado.QuantidadeMovimentacoes,
                NovasMovimentacoes: novasMovimentacoes));
    }

    public Task<HistoricoProcesso?> ObterHistoricoAsync(
        string numero,
        CancellationToken cancellationToken = default)
    {
        // Histórico é obtido através dos repositórios. Como o HistoricoProcesso
        // é um record de leitura usado pela camada de aplicação, delegamos para
        // um método de consulta composto.
        return ObterHistoricoCompostoAsync(numero, cancellationToken);
    }

    public async Task<IReadOnlyList<ProcessoMonitorado>> ListarProcessosAsync(
        CancellationToken cancellationToken = default)
    {
        var resumos = await _processos.ListarResumosAsync(cancellationToken);
        return resumos.Select(r => new ProcessoMonitorado(
            r.Numero,
            r.Tipo,
            r.PrimeiraConsultaUtc,
            r.UltimaConsultaUtc,
            r.TotalConsultas,
            r.TotalMovimentacoes,
            r.UnidadeAtual)).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<ProcessoAgendado>> ListarProcessosParaMonitorarAsync(
        CancellationToken cancellationToken = default)
    {
        var agendados = await _processos.ListarParaAgendamentoAsync(cancellationToken);
        return agendados.Select(r => new ProcessoAgendado(r.Numero, r.Fonte)).ToList().AsReadOnly();
    }

    private async Task<HistoricoProcesso?> ObterHistoricoCompostoAsync(
        string numero,
        CancellationToken cancellationToken)
    {
        var processo = await _processos.ObterPorNumeroAsync(numero, cancellationToken);
        if (processo is null)
            return null;

        var consultasEntidades = await _consultas.ObterPorProcessoAsync(numero, cancellationToken);

        var consultas = consultasEntidades.Select(c => new ConsultaRegistrada(
            Id: c.Id,
            ConsultadoEmUtc: c.ConsultadoEmUtc,
            QuantidadeProtocolos: c.QuantidadeProtocolos,
            QuantidadeMovimentacoes: c.QuantidadeMovimentacoes,
            UnidadeAtual: c.UnidadeAtual)).ToList();

        var movimentacoes = processo.Movimentacoes.Select(m => new MovimentacaoRegistrada(
            DataHora: m.DataHora,
            Unidade: m.Unidade,
            UnidadeRelacionada: m.UnidadeRelacionada,
            TipoEvento: m.TipoEvento,
            Descricao: m.Descricao,
            DetectadaEmUtc: m.DetectadaEmUtc,
            ConsultaId: m.ConsultaId)).ToList();

        return new HistoricoProcesso(
            Numero: processo.Numero,
            Tipo: processo.Tipo,
            DataGeracao: processo.DataGeracao,
            Interessados: processo.Interessados,
            AcessoRestrito: processo.AcessoRestrito,
            PrimeiraConsultaUtc: processo.PrimeiraConsultaUtc,
            UltimaConsultaUtc: processo.UltimaConsultaUtc,
            TotalConsultas: consultas.Count,
            Consultas: consultas,
            Movimentacoes: movimentacoes);
    }

    private static string CalcularChaveProtocolo(Domain.Protocolo protocolo) =>
        Hash(string.Join(
            '\u001f',
            protocolo.Numero,
            protocolo.Natureza,
            protocolo.Tipo,
            protocolo.Data,
            protocolo.DataInclusao,
            protocolo.Unidade,
            protocolo.AcessoRestrito ? "1" : "0"));

    private static string CalcularChaveMovimentacao(Domain.Movimentacao movimentacao) =>
        Hash(string.Join(
            '\u001f',
            movimentacao.DataHora.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            movimentacao.Unidade,
            movimentacao.UnidadeRelacionada,
            movimentacao.TipoEvento,
            movimentacao.Descricao));

    private static string Hash(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();
}
