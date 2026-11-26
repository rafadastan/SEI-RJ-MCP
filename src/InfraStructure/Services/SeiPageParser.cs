using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SeiRjMonitor.Application.Abstractions;
using SeiRjMonitor.Domain;

namespace SeiRjMonitor.Infrastructure.Services;

/// <summary>
/// Extrai os dados públicos do HTML da consulta processual do SEI-RJ.
/// </summary>
public sealed class SeiPageParser : ISeiPageParser
{
    public ConsultaProcessoResultado Parse(
        string html,
        string fonte,
        DateTimeOffset consultadoEmUtc)
    {
        var document = ParseHtml(html);
        var bodyText = SeiTextos.Normalizar(document.Body?.TextContent);

        if (bodyText.Contains("Processo não encontrado", StringComparison.OrdinalIgnoreCase))
        {
            return ConsultaProcessoResultado.Erro(
                FalhaConsultaTipo.ProcessoNaoEncontrado,
                "O SEI-RJ informou que o processo não foi encontrado. " +
                "O link pode ter expirado ou o processo pode ter deixado a consulta pública.");
        }

        var headerTable = document.QuerySelector("#tblCabecalho");

        if (headerTable is null
            && bodyText.Contains("Digite o código da imagem", StringComparison.OrdinalIgnoreCase))
        {
            return ConsultaProcessoResultado.Erro(
                FalhaConsultaTipo.RequerInteracaoHumana,
                "A página exige validação visual (CAPTCHA) ou não disponibilizou os dados do processo.");
        }

        var header = ParseHeaderTable(headerTable);
        var protocolos = ParseProtocolRows(document);
        var movimentacoes = ParseMovementRows(document);

        if (headerTable is null && protocolos.Count == 0 && movimentacoes.Count == 0)
        {
            return ConsultaProcessoResultado.Erro(
                FalhaConsultaTipo.LayoutDesconhecido,
                "A página foi carregada, mas não foram encontrados os blocos de processo, protocolos ou andamentos. " +
                "O formato da página pode ter mudado ou o acesso pode exigir autenticação.");
        }

        var numero = header.GetValueOrDefault("Processo")
            ?? SeiTextos.ExtrairNumeroProcesso(bodyText);

        var tipo = header.GetValueOrDefault("Tipo");
        var dataGeracao = header.GetValueOrDefault("Data de Geração")
            ?? header.GetValueOrDefault("Data de Geracao");
        var interessados = header.GetValueOrDefault("Interessados");

        var acessoRestrito =
            (headerTable is not null
             && (document.QuerySelector("#tblCabecalho img[title*='Acesso Restrito']") is not null
                 || document.QuerySelector("#tblCabecalho img[src*='chave_restrito']") is not null))
            || bodyText.Contains("Processo ou Documento de Acesso Restrito", StringComparison.OrdinalIgnoreCase);

        var processo = new ProcessoConsultado(
            Fonte: fonte,
            ConsultadoEmUtc: consultadoEmUtc,
            Numero: numero,
            Tipo: tipo,
            DataGeracao: dataGeracao,
            Interessados: interessados,
            AcessoRestrito: acessoRestrito,
            ProtocolosDeclarados: ParseDeclaredCount(document, "#tblDocumentos"),
            AndamentosDeclarados: ParseDeclaredCount(document, "#tblHistorico"),
            Protocolos: protocolos,
            Movimentacoes: movimentacoes);

        return ConsultaProcessoResultado.Ok(processo);
    }

    private static IDocument ParseHtml(string html)
    {
        // A página já foi decodificada a partir dos bytes originais (iso-8859-1).
        // Entregar a string pronta ao parser evita uma segunda decodificação pelo
        // <meta charset=iso-8859-1> da página, que transformava "Elaboração" em
        // "ElaboraÃ§Ã£o" (o conteúdo era reencodado em UTF-8 e relido como Latin-1).
        var parser = new HtmlParser(new HtmlParserOptions(), BrowsingContext.New(Configuration.Default));
        return parser.ParseDocument(html);
    }

    private static Dictionary<string, string> ParseHeaderTable(IElement? table)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (table is null) return map;

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("th,td").ToArray();
            if (cells.Length < 2) continue;

            var label = SeiTextos.Normalizar(cells[0].TextContent).TrimEnd(':').Trim();
            if (label.Length == 0) continue;

            map[label] = SeiTextos.Normalizar(cells[1].TextContent);
        }

        return map;
    }

    private static List<Protocolo> ParseProtocolRows(IDocument document)
    {
        var result = new List<Protocolo>();

        var table = document.QuerySelector("#tblDocumentos")
            ?? FindTableByHeader(document, "Processo / Documento");

        if (table is null) return result;

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("th,td").ToArray();

            // Linha de cabeçalho e linhas incompletas.
            if (cells.Length < 6) continue;
            if (cells.Any(c => c.TagName.Equals("TH", StringComparison.OrdinalIgnoreCase))) continue;

            // O HTML do SEI é malformado (tds sem fechamento). A AngleSharp preserva
            // a ordem, então a leitura é posicional:
            // [0] checkbox | [1] nº do protocolo | [2] tipo | [3] data | [4] inclusão | [5] unidade | [6] ações
            // Os atributos data-label chegam rotacionados entre as linhas e não são confiáveis.
            var numero = SeiTextos.Normalizar(
                cells[1].QuerySelector("span.retiraAncoraPadraoAzul")?.TextContent
                ?? cells[1].TextContent);

            if (!SeiTextos.EhNumeroProtocolo(numero)) continue;

            // A restrição de um documento é sinalizada apenas por ícone, sem texto.
            var restrito = row.QuerySelector(
                "img[title*='Acesso Restrito'], img[src*='chave_restrito']") is not null;

            result.Add(new Protocolo(
                Numero: numero,
                Natureza: numero.StartsWith("SEI-", StringComparison.OrdinalIgnoreCase)
                    ? "Processo"
                    : "Documento",
                Tipo: SeiTextos.NuloSeVazio(cells[2].TextContent),
                Data: SeiTextos.NuloSeVazio(cells[3].TextContent),
                DataInclusao: SeiTextos.NuloSeVazio(cells[4].TextContent),
                Unidade: SeiTextos.NuloSeVazio(cells[5].TextContent),
                AcessoRestrito: restrito));
        }

        return result;
    }

    private static List<Movimentacao> ParseMovementRows(IDocument document)
    {
        var result = new List<Movimentacao>();

        var table = document.QuerySelector("#tblHistorico")
            ?? FindTableByHeader(document, "Data/Hora");

        if (table is null) return result;

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.QuerySelectorAll("th,td").ToArray();
            if (cells.Length < 3) continue;

            var values = cells.Select(c => SeiTextos.Normalizar(c.TextContent)).ToArray();
            if (!SeiTextos.TryParseDataHora(values[0], out var dataHora)) continue;

            var descricao = values[2];
            if (string.IsNullOrWhiteSpace(descricao)) continue;

            // [0] data/hora | [1] unidade que registrou | [2] descrição.
            // A unidade é um <a class="ancoraSigla">; a descrição pode citar outra unidade no mesmo formato.
            var unidade = SeiTextos.Normalizar(
                cells[1].QuerySelector("a.ancoraSigla")?.TextContent
                ?? cells[1].TextContent);

            var unidadeRelacionada = SeiTextos.Normalizar(
                cells[2].QuerySelector("a.ancoraSigla")?.TextContent ?? string.Empty);

            result.Add(new Movimentacao(
                DataHora: dataHora,
                Unidade: unidade,
                UnidadeRelacionada: string.IsNullOrWhiteSpace(unidadeRelacionada) ? null : unidadeRelacionada,
                TipoEvento: ClassificadorEvento.Classificar(descricao),
                Descricao: descricao));
        }

        return result
            .OrderBy(m => m.DataHora)
            .ToList();
    }

    private static IElement? FindTableByHeader(IDocument document, string headerNeedle)
    {
        foreach (var table in document.QuerySelectorAll("table"))
        {
            var firstRow = table.QuerySelector("tr");
            var headerText = SeiTextos.Normalizar(firstRow?.TextContent ?? string.Empty);
            if (headerText.Contains(headerNeedle, StringComparison.OrdinalIgnoreCase))
                return table;
        }

        return null;
    }

    private static int? ParseDeclaredCount(IDocument document, string tableSelector)
    {
        var caption = document.QuerySelector($"{tableSelector} caption");
        return caption is null ? null : SeiTextos.ExtrairQuantidadeRegistros(caption.TextContent);
    }
}
