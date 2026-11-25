using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Domain.ValueObjects;

namespace SeiRjMonitor.Domain.Interfaces;

/// <summary>
/// Contrato de persistência para a raiz do agregado Processo.
/// </summary>
public interface IProcessoRepository
{
    /// <summary>
    /// Recupera um processo pelo número, ou <c>null</c> se nunca foi consultado.
    /// </summary>
    Task<Processo?> ObterPorNumeroAsync(string numero, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adiciona um novo processo ao repositório.
    /// </summary>
    Task AdicionarAsync(Processo processo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atualiza um processo existente.
    /// </summary>
    Task AtualizarAsync(Processo processo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista os resumos dos processos monitorados, do mais recente para o mais antigo.
    /// </summary>
    Task<IReadOnlyList<ProcessoResumo>> ListarResumosAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista os processos com o link (fonte) da última consulta, ordenados
    /// do mais antigo para o mais recente, para uso do agendador.
    /// </summary>
    Task<IReadOnlyList<ProcessoParaAgendamento>> ListarParaAgendamentoAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resumo de um processo monitorado, usado na listagem.
/// </summary>
public record ProcessoResumo(
    string Numero,
    string? Tipo,
    DateTimeOffset PrimeiraConsultaUtc,
    DateTimeOffset UltimaConsultaUtc,
    int TotalConsultas,
    int TotalMovimentacoes,
    string? UnidadeAtual);

/// <summary>
/// Dados mínimos de um processo para agendamento de nova consulta.
/// </summary>
public record ProcessoParaAgendamento(string Numero, string Fonte);
