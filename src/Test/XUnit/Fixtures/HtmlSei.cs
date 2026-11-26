namespace SeiRjMonitor.Tests.Fixtures;

/// <summary>
/// Gera um HTML mínimo com a mesma estrutura de tabelas do SEI-RJ usada pelo parser.
/// </summary>
internal static class HtmlSei
{
    public static string Criar(string numero, params string[] descricoes)
    {
        var movimentacoes = descricoes.Length == 0
            ? "Recebimento do processo"
            : string.Join(
                '\n',
                descricoes.Select((descricao, indice) =>
                    $"<tr><td>{new DateTime(2024, 1, 10).AddDays(indice):dd/MM/yyyy HH:mm}</td>" +
                    "<td><a class=\"ancoraSigla\">UNIDADE/TESTE</a></td>" +
                    $"<td>{descricao}</td></tr>"));

        var quantidade = Math.Max(descricoes.Length, 1);

        return $@"<!DOCTYPE html>
<html><body>
<table id=""tblCabecalho"">
  <tr><td>Processo</td><td>{numero}</td></tr>
  <tr><td>Tipo</td><td>Processo</td></tr>
  <tr><td>Data de Geração</td><td>01/01/2024</td></tr>
  <tr><td>Interessados</td><td>TESTE DA SILVA</td></tr>
</table>
<table id=""tblDocumentos"">
  <caption>1 registro</caption>
  <tr><th>Documento</th><th>Tipo</th><th>Data</th><th>Inclusão</th><th>Unidade</th></tr>
  <tr><td></td><td>74146277</td><td>Documento</td><td>15/01/2024</td><td>15/01/2024 10:00</td><td>UNIDADE/TESTE</td></tr>
</table>
<table id=""tblHistorico"">
  <caption>{quantidade} registro(s)</caption>
  <tr><th>Data/Hora</th><th>Unidade</th><th>Descrição</th></tr>
  {movimentacoes}
</table>
</body></html>";
    }
}
