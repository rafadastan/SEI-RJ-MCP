using SeiRjMonitor.Domain;

namespace SeiRjMonitor.Application.Persistence;

/// <summary>
/// Resultado do registro de uma consulta no banco local.
/// </summary>
public sealed record ResultadoPersistencia(
    long ProcessoId,
    long ConsultaId,
    bool PrimeiraConsulta,
    int ProtocolosNovos,
    int ProtocolosTotal,
    int MovimentacoesNovas,
    int MovimentacoesTotal,
    IReadOnlyList<Movimentacao> NovasMovimentacoes);

/// <summary>
/// Consulta individual registrada para um processo.
/// </summary>
public sealed record ConsultaRegistrada(
    long Id,
    DateTimeOffset ConsultadoEmUtc,
    int QuantidadeProtocolos,
    int QuantidadeMovimentacoes,
    string? UnidadeAtual);

/// <summary>
/// Movimentação persistida, com o carimbo da consulta que a detectou.
/// </summary>
public sealed record MovimentacaoRegistrada(
    DateTime DataHora,
    string Unidade,
    string? UnidadeRelacionada,
    string TipoEvento,
    string Descricao,
    DateTimeOffset DetectadaEmUtc,
    long ConsultaId);

/// <summary>
/// Histórico local completo de um processo monitorado.
/// </summary>
public sealed record HistoricoProcesso(
    string Numero,
    string? Tipo,
    string? DataGeracao,
    string? Interessados,
    bool AcessoRestrito,
    DateTimeOffset PrimeiraConsultaUtc,
    DateTimeOffset UltimaConsultaUtc,
    int TotalConsultas,
    IReadOnlyList<ConsultaRegistrada> Consultas,
    IReadOnlyList<MovimentacaoRegistrada> Movimentacoes);

/// <summary>
/// Resumo de um processo já armazenado, usado na listagem.
/// </summary>
public sealed record ProcessoMonitorado(
    string Numero,
    string? Tipo,
    DateTimeOffset PrimeiraConsultaUtc,
    DateTimeOffset UltimaConsultaUtc,
    int TotalConsultas,
    int TotalMovimentacoes,
    string? UnidadeAtual);

/// <summary>
/// Processo já armazenado com o link usado na consulta mais recente,
/// informação necessária para o agendador repetir a coleta.
/// </summary>
public sealed record ProcessoAgendado(
    string Numero,
    string Url);
