namespace SeiRjMonitor.Domain;

public enum FalhaConsultaTipo
{
    LinkInvalido,
    FalhaHttp,
    PaginaVazia,
    ProcessoNaoEncontrado,
    RequerInteracaoHumana,
    LayoutDesconhecido
}

public sealed record FalhaConsulta(
    FalhaConsultaTipo Tipo,
    string Mensagem,
    int? StatusHttp = null);

/// <summary>
/// Resultado da consulta: ou um processo extraído, ou uma falha explicada.
/// </summary>
public sealed record ConsultaProcessoResultado(
    ProcessoConsultado? Processo,
    FalhaConsulta? Falha)
{
    public bool Sucesso => Processo is not null;

    public static ConsultaProcessoResultado Ok(ProcessoConsultado processo) =>
        new(processo, null);

    public static ConsultaProcessoResultado Erro(
        FalhaConsultaTipo tipo,
        string mensagem,
        int? statusHttp = null) =>
        new(null, new FalhaConsulta(tipo, mensagem, statusHttp));
}
