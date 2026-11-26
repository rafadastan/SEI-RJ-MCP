using SeiRjMonitor.Domain;
using Xunit;

namespace SeiRjMonitor.Tests.DomainTests;

public class SeiLinkTests
{
    private const string Caminho = "/sei/modulos/pesquisa/md_pesq_processo_exibir.php";

    [Theory]
    [InlineData("https://sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?processo_aux=123")]
    [InlineData("https://baixada.sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?123456")]
    [InlineData("https://sub.dominio.sei.rj.gov.br/sei/modulos/pesquisa/md_pesq_processo_exibir.php?token=abc")]
    [InlineData("https://sei.rj.gov.br:443/sei/modulos/pesquisa/md_pesq_processo_exibir.php?abc")]
    public void DeveValidarLinksCorretosDoSeiRj(string url)
    {
        var valido = SeiLink.TryParse(url, out var link, out var erro);

        Assert.True(valido);
        Assert.NotNull(link);
        Assert.Equal(string.Empty, erro);
    }

    [Theory]
    [InlineData("http://sei.rj.gov.br" + Caminho + "?123")]
    [InlineData("https://malicious.com" + Caminho + "?123")]
    [InlineData("https://sei.rj.gov.br.malicious.com" + Caminho + "?123")]
    [InlineData("https://evilsei.rj.gov.br" + Caminho + "?123")]
    [InlineData("https://sei.rj.gov.br/outro/caminho/arquivo.php?123")]
    [InlineData("https://sei.rj.gov.br/prefixo" + Caminho + "?123")]
    [InlineData("https://sei.rj.gov.br" + Caminho + "/?123")]
    [InlineData("https://usuario:senha@sei.rj.gov.br" + Caminho + "?123")]
    [InlineData("https://sei.rj.gov.br:8443" + Caminho + "?123")]
    [InlineData("https://localhost" + Caminho + "?123")]
    [InlineData("https://127.0.0.1" + Caminho + "?123")]
    [InlineData("https://10.0.0.1" + Caminho + "?123")]
    [InlineData("https://169.254.169.254" + Caminho + "?123")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://sei.rj.gov.br" + Caminho)]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData(null)]
    public void DeveRejeitarLinksInvalidosOuSuspeitos(string? url)
    {
        var valido = SeiLink.TryParse(url, out var link, out var erro);

        Assert.False(valido);
        Assert.Null(link);
        Assert.False(string.IsNullOrWhiteSpace(erro));
    }

    [Fact]
    public void DeveClassificarParametroDeConsulta()
    {
        Assert.True(SeiLink.TryParse(
            "https://sei.rj.gov.br" + Caminho + "?processo_aux=123",
            out var nomeado, out _));
        Assert.Equal("parametro-nomeado", nomeado!.FormatoParametro);
        Assert.True(nomeado.PossuiParametroDeConsulta);

        Assert.True(SeiLink.TryParse(
            "https://sei.rj.gov.br" + Caminho + "?tokenopaco",
            out var opaco, out _));
        Assert.Equal("token-opaco", opaco!.FormatoParametro);

        Assert.True(SeiLink.TryParse(
            "https://sei.rj.gov.br" + Caminho,
            out var semParametro, out _));
        Assert.Equal("sem-parametro", semParametro!.FormatoParametro);
        Assert.False(semParametro.PossuiParametroDeConsulta);
    }
}
