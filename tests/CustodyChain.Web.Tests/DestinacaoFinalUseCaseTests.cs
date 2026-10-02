using System.Text.Json;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.DestinacaoFinal;

namespace CustodyChain.Web.Tests;

public sealed class DestinacaoFinalUseCaseTests
{
    [Fact]
    public async Task Solicitar_ComDadosValidos_ArmazenaAutorizacaoEPersisteSolicitacao()
    {
        var store = new StoreFake();
        var armazenamento = new ArmazenamentoFake();
        var useCase = new SolicitarDestinacaoUseCase(store, armazenamento, new ClockFixo());

        var resultado = await useCase.ExecutarAsync(new SolicitarDestinacaoCommand(
            3, 42, "DESCARTE", " did:legal:judge:001 ", " mandado.pdf ", [1, 2, 3], " Motivo "));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal("mandado.pdf", armazenamento.NomeArquivo);
        var solicitacao = Assert.IsType<SolicitacaoDestinacaoPendente>(store.SolicitacaoPersistida);
        Assert.Equal("DESCARTE", solicitacao.Tipo);
        Assert.Equal("did:legal:judge:001", solicitacao.DidMagistrado);
        Assert.Equal("cid-autorizacao", solicitacao.CidAutorizacao);
        Assert.Equal(64, solicitacao.HashAutorizacao.Length);
    }

    [Fact]
    public async Task Solicitar_SemAutorizacao_NaoArmazenaNemPersiste()
    {
        var store = new StoreFake();
        var armazenamento = new ArmazenamentoFake();
        var useCase = new SolicitarDestinacaoUseCase(store, armazenamento, new ClockFixo());

        await Assert.ThrowsAsync<ValidacaoDestinacaoFinalException>(() => useCase.ExecutarAsync(
            new SolicitarDestinacaoCommand(3, 42, "DESCARTE", "did:legal:judge:001", null, null, null)));

        Assert.False(armazenamento.FoiChamado);
        Assert.Null(store.SolicitacaoPersistida);
    }

    [Fact]
    public async Task Aprovar_ComSolicitacaoDeOutroAtor_PersisteEncerramentoPendente()
    {
        var store = new StoreFake();
        var useCase = new AprovarDestinacaoUseCase(store, new ClockFixo(), new GeradorCredencialFixo());

        var resultado = await useCase.ExecutarAsync(new AprovarDestinacaoCommand(4, 17));

        Assert.Equal("DESCARTE", resultado.Tipo);
        Assert.True(resultado.AncoragemPendente);
        var aprovacao = Assert.IsType<AprovacaoDestinacaoPendente>(store.AprovacaoPersistida);
        Assert.Equal("cred-coc-destinacao", aprovacao.CredencialId);
        Assert.Equal(64, aprovacao.PayloadHashSha256.Length);
        using var payload = JsonDocument.Parse(aprovacao.PayloadJson);
        Assert.Equal("RE-001", payload.RootElement.GetProperty("RE").GetString());
        Assert.Equal("did:legal:admin:001", payload.RootElement.GetProperty("AprovadoPor").GetString());
    }

    [Fact]
    public async Task Aprovar_QuandoNaoHaSolicitacaoAprovavel_NaoPersiste()
    {
        var store = new StoreFake { ContextoAprovacao = null };
        var useCase = new AprovarDestinacaoUseCase(store, new ClockFixo(), new GeradorCredencialFixo());

        await Assert.ThrowsAsync<RecursoDestinacaoFinalNaoEncontradoException>(() =>
            useCase.ExecutarAsync(new AprovarDestinacaoCommand(3, 17)));

        Assert.Null(store.AprovacaoPersistida);
    }

    private sealed class StoreFake : IDestinacaoFinalStore
    {
        public ContextoAprovacaoDestinacao? ContextoAprovacao { get; init; } = new(
            17, 42, "RE-001", "DESCARTE", "did:legal:judge:001", "did:legal:admin:001");
        public SolicitacaoDestinacaoPendente? SolicitacaoPersistida { get; private set; }
        public AprovacaoDestinacaoPendente? AprovacaoPersistida { get; private set; }

        public Task<ContextoSolicitacaoDestinacao?> ObterContextoSolicitacaoAsync(
            long vestigioId, long solicitanteId, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoSolicitacaoDestinacao?>(new(42, "RE-001"));

        public Task PersistirSolicitacaoAsync(SolicitacaoDestinacaoPendente solicitacao, CancellationToken cancellationToken)
        {
            SolicitacaoPersistida = solicitacao;
            return Task.CompletedTask;
        }

        public Task<ContextoAprovacaoDestinacao?> ObterContextoAprovacaoAsync(
            long descarteId, long aprovadorId, CancellationToken cancellationToken) =>
            Task.FromResult(ContextoAprovacao);

        public Task PersistirAprovacaoAsync(AprovacaoDestinacaoPendente aprovacao, CancellationToken cancellationToken)
        {
            AprovacaoPersistida = aprovacao;
            return Task.CompletedTask;
        }
    }

    private sealed class ArmazenamentoFake : IArmazenamentoAutorizacao
    {
        public bool FoiChamado { get; private set; }
        public string? NomeArquivo { get; private set; }

        public Task<AutorizacaoArmazenada> ArmazenarAsync(byte[] conteudo, string nomeArquivo, CancellationToken cancellationToken = default)
        {
            FoiChamado = true;
            NomeArquivo = nomeArquivo;
            return Task.FromResult(new AutorizacaoArmazenada("cid-autorizacao", conteudo.Length));
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class GeradorCredencialFixo : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => "cred-coc-destinacao";
    }
}
