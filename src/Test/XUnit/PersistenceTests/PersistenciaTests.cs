using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SeiRjMonitor.Domain;
using SeiRjMonitor.Domain.Aggregates;
using SeiRjMonitor.Infrastructure.Persistence;
using SeiRjMonitor.Infrastructure.Repositories;
using SeiRjMonitor.Tests.Fixtures;
using Xunit;

namespace SeiRjMonitor.Tests.PersistenceTests;

public class PersistenciaTests
{
    private static readonly DateTimeOffset Agora = new(2024, 1, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DevePersistirProcessoConsultaProtocoloEMovimentacao()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var ctx = banco.Contexto;

        var processo = new Processo("SEI-260005/005504/2024", Agora);
        processo.AtualizarDados("Processo", "01/01/2024", "TESTE", false, "UNIDADE/TESTE", Agora);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var consulta = processo.AdicionarConsulta(
            Agora, "https://sei.rj.gov.br/link", 1, 1, 1, 1, "UNIDADE/TESTE");
        ctx.Consultas.Add(consulta);
        await ctx.SaveChangesAsync();

        var protocolo = new Protocolo(
            "74146277", "Documento", "Documento", "15/01/2024", "15/01/2024 10:00",
            "UNIDADE/TESTE", false);
        processo.AdicionarProtocolo(new ProtocoloItem(processo.Id, "chave-protocolo", protocolo, Agora));

        var movimentacao = new Movimentacao(
            new DateTime(2024, 1, 10, 9, 0, 0), "UNIDADE/TESTE", null,
            "Recebimento", "Recebimento do processo");
        processo.AdicionarMovimentacao(
            new MovimentacaoItem(processo.Id, consulta.Id, "chave-mov", movimentacao, Agora));

        ctx.Processos.Update(processo);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();

        var repo = new ProcessoRepository(ctx);
        var carregado = await repo.ObterPorNumeroAsync("SEI-260005/005504/2024");

        Assert.NotNull(carregado);
        Assert.Single(carregado!.Consultas);
        Assert.Single(carregado.Protocolos);
        Assert.Single(carregado.Movimentacoes);
        Assert.Equal("UNIDADE/TESTE", carregado.UnidadeAtual);
    }

    [Fact]
    public async Task DeveConsultarChaveDeMovimentacaoApenasNoProcessoInformado()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var ctx = banco.Contexto;

        var processoA = new Processo("SEI-260005/000001/2024", Agora);
        var processoB = new Processo("SEI-260005/000002/2024", Agora);
        ctx.Processos.AddRange(processoA, processoB);
        await ctx.SaveChangesAsync();

        var movimentacao = new Movimentacao(
            new DateTime(2024, 1, 10, 9, 0, 0), "UNIDADE", null, "Recebimento", "Igual");
        processoA.AdicionarMovimentacao(
            new MovimentacaoItem(processoA.Id, 0, "chave-compartilhada", movimentacao, Agora));
        ctx.Processos.Update(processoA);
        await ctx.SaveChangesAsync();

        var repo = new MovimentacaoRepository(ctx);

        Assert.True(await repo.ExisteChaveAsync(processoA.Id, "chave-compartilhada"));
        Assert.False(await repo.ExisteChaveAsync(processoB.Id, "chave-compartilhada"));
    }

    [Fact]
    public async Task DeveConsultarChaveDeProtocoloApenasNoProcessoInformado()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var ctx = banco.Contexto;

        var processoA = new Processo("SEI-260005/000001/2024", Agora);
        var processoB = new Processo("SEI-260005/000002/2024", Agora);
        ctx.Processos.AddRange(processoA, processoB);
        await ctx.SaveChangesAsync();

        var protocolo = new Protocolo(
            "74146277", "Documento", "Documento", "15/01/2024", "15/01/2024 10:00",
            "UNIDADE", false);
        processoA.AdicionarProtocolo(
            new ProtocoloItem(processoA.Id, "chave-compartilhada", protocolo, Agora));
        ctx.Processos.Update(processoA);
        await ctx.SaveChangesAsync();

        var repo = new ProtocoloRepository(ctx);

        Assert.True(await repo.ExisteChaveAsync(processoA.Id, "chave-compartilhada"));
        Assert.False(await repo.ExisteChaveAsync(processoB.Id, "chave-compartilhada"));
    }

    [Fact]
    public async Task DeveImpedirMovimentacaoDuplicadaNoMesmoProcesso()
    {
        // SQLite relacional é usado aqui apenas para validar a constraint única,
        // que o provider in-memory não aplica.
        await using var conexao = new SqliteConnection("DataSource=:memory:");
        await conexao.OpenAsync();

        var options = new DbContextOptionsBuilder<SeiRjMonitorDbContext>()
            .UseSqlite(conexao)
            .Options;

        await using var ctx = new SeiRjMonitorDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        var processo = new Processo("SEI-260005/005504/2024", Agora);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var movimentacao = new Movimentacao(
            new DateTime(2024, 1, 10, 9, 0, 0), "UNIDADE", null, "Recebimento", "Igual");
        processo.AdicionarMovimentacao(
            new MovimentacaoItem(processo.Id, 0, "chave-duplicada", movimentacao, Agora));
        ctx.Processos.Update(processo);
        await ctx.SaveChangesAsync();

        ctx.Movimentacoes.Add(
            new MovimentacaoItem(processo.Id, 0, "chave-duplicada", movimentacao, Agora));

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task DeveRetornarNuloQuandoProcessoNaoExiste()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var repo = new ProcessoRepository(banco.Contexto);

        var processo = await repo.ObterPorNumeroAsync("SEI-000000/000000/2024");

        Assert.Null(processo);
    }

    [Fact]
    public async Task DeveListarResumosComContagens()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var ctx = banco.Contexto;

        var processo = new Processo("SEI-260005/005504/2024", Agora);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        var consulta = processo.AdicionarConsulta(
            Agora, "https://sei.rj.gov.br/link", null, null, 0, 1, "UNIDADE");
        ctx.Consultas.Add(consulta);
        await ctx.SaveChangesAsync();

        processo.AdicionarMovimentacao(new MovimentacaoItem(
            processo.Id,
            consulta.Id,
            "chave",
            new Movimentacao(new DateTime(2024, 1, 10), "UNIDADE", null, "Outro", "Teste"),
            Agora));
        ctx.Processos.Update(processo);
        await ctx.SaveChangesAsync();

        var repo = new ProcessoRepository(ctx);
        var resumos = await repo.ListarResumosAsync();

        var resumo = Assert.Single(resumos);
        Assert.Equal(1, resumo.TotalConsultas);
        Assert.Equal(1, resumo.TotalMovimentacoes);
    }

    [Fact]
    public async Task DeveUsarFonteDaConsultaMaisRecenteNoAgendamento()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var ctx = banco.Contexto;

        var processo = new Processo("SEI-260005/005504/2024", Agora);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        ctx.Consultas.Add(processo.AdicionarConsulta(
            Agora.AddHours(-1), "https://sei.rj.gov.br/antigo", null, null, 0, 0, null));
        ctx.Consultas.Add(processo.AdicionarConsulta(
            Agora, "https://sei.rj.gov.br/novo", null, null, 0, 0, null));
        await ctx.SaveChangesAsync();

        var repo = new ProcessoRepository(ctx);
        var agendados = await repo.ListarParaAgendamentoAsync();

        var agendado = Assert.Single(agendados);
        Assert.Equal("https://sei.rj.gov.br/novo", agendado.Fonte);
    }

    [Fact]
    public async Task DeveOrdenarConsultasPorData()
    {
        await using var banco = await BancoTemporario.CriarAsync();
        var ctx = banco.Contexto;

        var processo = new Processo("SEI-260005/005504/2024", Agora);
        ctx.Processos.Add(processo);
        await ctx.SaveChangesAsync();

        ctx.Consultas.Add(processo.AdicionarConsulta(
            Agora, "https://sei.rj.gov.br/segunda", null, null, 0, 0, null));
        ctx.Consultas.Add(processo.AdicionarConsulta(
            Agora.AddHours(-2), "https://sei.rj.gov.br/primeira", null, null, 0, 0, null));
        await ctx.SaveChangesAsync();

        var repo = new ConsultaRepository(ctx);
        var consultas = await repo.ObterPorProcessoAsync(processo.Numero);

        Assert.Equal(2, consultas.Count);
        Assert.Equal("https://sei.rj.gov.br/primeira", consultas[0].Fonte);
        Assert.Equal("https://sei.rj.gov.br/segunda", consultas[1].Fonte);
    }
}
