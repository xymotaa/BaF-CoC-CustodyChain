using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.RevogacaoDid;

namespace CustodyChain.Web.Tests;

public sealed class RevogacaoDidUseCasesTests
{
    [Fact]
    public async Task ExecutarAsync_ConfirmaSagaQuandoLedgerAceitaProva()
    {
        var store = new StoreFake();
        var ledger = new LedgerFake();
        var useCase = CriarRevogar(store, ledger);

        var resultado = await useCase.ExecutarAsync(Comando());

        Assert.Equal(EstadoRevogacaoDid.ConfirmadaNoLedger, resultado.Estado);
        Assert.Equal(1, ledger.Revogacoes);
        Assert.Contains("DID_REVOGACAO_CONFIRMADA", store.Acoes);
    }

    [Fact]
    public async Task ExecutarAsync_ConservaSagaParaRetryQuandoLedgerIndisponivel()
    {
        var store = new StoreFake();
        var ledger = new LedgerFake { Falha = new HttpRequestException("gateway indisponível") };
        var useCase = CriarRevogar(store, ledger);

        await Assert.ThrowsAsync<LedgerRevogacaoDidIndisponivelException>(() => useCase.ExecutarAsync(Comando()));

        Assert.Equal(EstadoRevogacaoDid.FalhaReconciliacao, store.Saga!.Estado);
        Assert.Contains("DID_REVOGACAO_RECONCILIACAO_FALHOU", store.Acoes);
    }

    [Fact]
    public async Task ExecutarAsync_NaoReenviaCommandIdJaConfirmado()
    {
        var store = new StoreFake { Saga = Saga(EstadoRevogacaoDid.ConfirmadaNoLedger) };
        var ledger = new LedgerFake();
        var useCase = CriarRevogar(store, ledger);

        var resultado = await useCase.ExecutarAsync(Comando());

        Assert.Equal(EstadoRevogacaoDid.ConfirmadaNoLedger, resultado.Estado);
        Assert.Equal(0, ledger.Revogacoes);
    }

    [Fact]
    public async Task Reconciliar_SeparaRejeicaoDefinitivaDeFalhaTransitoria()
    {
        var store = new StoreFake { Pendencias = [Saga(EstadoRevogacaoDid.PendenteLedger)] };
        var ledger = new LedgerFake { Falha = new RevogacaoDidRejeitadaException("DID_REVOCATION_SIGNATURE_INVALID", "assinatura inválida") };
        var useCase = CriarReconciliador(store, ledger);

        await useCase.ExecutarLoteAsync();

        Assert.Equal(EstadoRevogacaoDid.FalhaPermanente, store.Saga!.Estado);
        Assert.Contains("DID_REVOGACAO_REJEITADA", store.Acoes);
    }

    [Fact]
    public async Task ExecutarAsync_ConvergeQuandoConflitoDeVersaoIndicaDidJaRevogado()
    {
        var store = new StoreFake();
        var ledger = new LedgerFake { Falha = ConflitoDeVersao() };
        var didRegistry = new DidRegistryFake { Documento = DocumentoRevogado() };
        var useCase = CriarRevogar(store, ledger, didRegistry);

        var resultado = await useCase.ExecutarAsync(Comando());

        Assert.Equal(EstadoRevogacaoDid.ConfirmadaNoLedger, resultado.Estado);
        Assert.Contains("DID_REVOGACAO_RECONCILIADA", store.Acoes);
    }

    [Fact]
    public async Task ExecutarAsync_RejeitaSemReativarQuandoConflitoDeVersaoMantemDidAtivo()
    {
        var store = new StoreFake();
        var ledger = new LedgerFake { Falha = ConflitoDeVersao() };
        var useCase = CriarRevogar(store, ledger, new DidRegistryFake { Documento = DocumentoAtivo() });

        var resultado = await useCase.ExecutarAsync(Comando());

        Assert.Equal(EstadoRevogacaoDid.FalhaPermanente, resultado.Estado);
        Assert.Contains("DID_REVOGACAO_REJEITADA", store.Acoes);
    }

    [Fact]
    public async Task ExecutarAsync_MantemSagaPendenteQuandoConflitoDeVersaoNaoPodeSerResolvido()
    {
        var store = new StoreFake();
        var ledger = new LedgerFake { Falha = ConflitoDeVersao() };
        var useCase = CriarRevogar(store, ledger, new DidRegistryFake { Documento = null });

        var resultado = await useCase.ExecutarAsync(Comando());

        Assert.Equal(EstadoRevogacaoDid.FalhaReconciliacao, resultado.Estado);
        Assert.Contains("DID_REVOGACAO_RECONCILIACAO_FALHOU", store.Acoes);
    }

    [Fact]
    public async Task Reconciliar_ConvergeQuandoConflitoDeVersaoIndicaDidJaRevogado()
    {
        var store = new StoreFake { Pendencias = [Saga(EstadoRevogacaoDid.PendenteLedger)] };
        var ledger = new LedgerFake { Falha = ConflitoDeVersao() };
        var useCase = CriarReconciliador(store, ledger, new DidRegistryFake { Documento = DocumentoRevogado() });

        await useCase.ExecutarLoteAsync();

        Assert.Equal(EstadoRevogacaoDid.ConfirmadaNoLedger, store.Saga!.Estado);
        Assert.Contains("DID_REVOGACAO_RECONCILIADA", store.Acoes);
    }

    [Fact]
    public async Task ExecutarAsync_AuditaComandoSemCommandIdAntesDaSaga()
    {
        var store = new StoreFake();
        var useCase = CriarRevogar(store, new LedgerFake());
        var semCommandId = Comando() with { Command = JsonSerializer.SerializeToElement(new { type = "CustodyChainDidRevocation" }) };

        await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() => useCase.ExecutarAsync(semCommandId));

        Assert.Contains("REJEICAO", store.Acoes);
        Assert.Null(store.Saga);
    }

    [Fact]
    public async Task ExecutarAsync_AuditaProvaAusenteAntesDaSaga()
    {
        var store = new StoreFake();
        var useCase = CriarRevogar(store, new LedgerFake());

        await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() => useCase.ExecutarAsync(Comando() with { KeyId = "", Signature = "" }));

        Assert.Contains("REJEICAO", store.Acoes);
        Assert.Null(store.Saga);
    }

    [Fact]
    public async Task PrepararAsync_AuditaDidInexistenteAntesDaSaga()
    {
        var store = new StoreFake();
        var useCase = new RevogarDidUseCase(store, new LedgerFake(), new DidRegistryFake { Documento = null }, new ClockFake(), new NonceFake());

        await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() => useCase.PrepararAsync(Preparar()));

        Assert.Contains("REJEICAO", store.Acoes);
        Assert.Null(store.Saga);
    }

    [Fact]
    public async Task PrepararAsync_AuditaDidInativoOuGovernancaInvalidaAntesDaSaga()
    {
        var store = new StoreFake();
        var documentoInativo = new DocumentoDidAutenticacao("did:legal:expert:b", 2, "inactive", false, [], [], 2);
        var useCase = new RevogarDidUseCase(store, new LedgerFake(), new DidRegistryFake { Documento = documentoInativo }, new ClockFake(), new NonceFake());

        await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() => useCase.PrepararAsync(Preparar()));

        Assert.Contains("REJEICAO", store.Acoes);
        Assert.Null(store.Saga);
    }

    [Fact]
    public async Task PrepararAsync_AuditaGovernancaInvalidaAntesDaSaga()
    {
        var store = new StoreFake();
        var useCase = new RevogarDidUseCase(store, new LedgerFake { Governanca = new GovernancaRevogacaoDid(1, []) }, new DidRegistryFake(), new ClockFake(), new NonceFake());

        await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() => useCase.PrepararAsync(Preparar()));

        Assert.Contains("REJEICAO", store.Acoes);
        Assert.Null(store.Saga);
    }

    [Fact]
    public async Task PrepararAsync_ConverteIndisponibilidadeDoResolverParaFalhaRecuperavel()
    {
        var store = new StoreFake();
        var didRegistry = new DidRegistryFake { Falha = new HttpRequestException("gateway indisponível") };
        var useCase = new RevogarDidUseCase(store, new LedgerFake(), didRegistry, new ClockFake(), new NonceFake());

        await Assert.ThrowsAsync<LedgerRevogacaoDidIndisponivelException>(() => useCase.PrepararAsync(Preparar()));

        Assert.Empty(store.Acoes);
    }

    [Fact]
    public async Task ExecutarAsync_AuditaConflitoDeCommandIdAntesDaSaga()
    {
        var store = new StoreFake { Saga = Saga(EstadoRevogacaoDid.PendenteLedger) with { CommandJson = "{}" } };
        var useCase = CriarRevogar(store, new LedgerFake());

        await Assert.ThrowsAsync<RevogacaoDidInvalidaException>(() => useCase.ExecutarAsync(Comando()));

        Assert.Contains("REJEICAO", store.Acoes);
    }

    private static RevogarDidUseCase CriarRevogar(StoreFake store, LedgerFake ledger, DidRegistryFake? didRegistry = null) =>
        new(store, ledger, didRegistry ?? new DidRegistryFake(), new ClockFake(), new NonceFake());

    private static ReconciliarRevogacaoDidUseCase CriarReconciliador(StoreFake store, LedgerFake ledger, DidRegistryFake? didRegistry = null) =>
        new(store, ledger, didRegistry ?? new DidRegistryFake(), new ClockFake());

    private static RevogacaoDidRejeitadaException ConflitoDeVersao() =>
        new(CodigosRevogacaoDid.ConflitoVersaoTitular, "DID não está ativo na versão esperada.");

    private static DocumentoDidAutenticacao DocumentoAtivo() =>
        new("did:legal:expert:b", 2, "ATIVO", true, [], [], 3);

    private static DocumentoDidAutenticacao DocumentoRevogado() =>
        new("did:legal:expert:b", 2, "REVOGADO", false, [], [], 3);

    private static ExecutarRevogacaoDidCommand Comando() => new(11, 1, "did:legal:admin:a",
        JsonSerializer.SerializeToElement(new { type = "CustodyChainDidRevocation", commandId = "urn:uuid:11111111-1111-1111-1111-111111111111",
            actorDid = "did:legal:admin:a", subjectDid = "did:legal:expert:b", expectedDocumentVersion = 2, expectedGovernanceRevision = 1 }),
        "did:legal:admin:a#key-1", "assinatura", new ContextoAuditoriaRevogacaoDid("127.0.0.1", "test"));

    private static PrepararRevogacaoDidCommand Preparar() => new(11, 1, "did:legal:admin:a", new ContextoAuditoriaRevogacaoDid("127.0.0.1", "test"));

    private static SagaRevogacaoDidDto Saga(EstadoRevogacaoDid estado) => new("urn:uuid:11111111-1111-1111-1111-111111111111",
        "did:legal:expert:b", CustodyChain.Web.Application.SignedOperations.CanonicalizadorJsonCustodyChain.Canonicalizar(Comando().Command),
        "did:legal:admin:a#key-1", "assinatura", estado, "Perito B");

    private sealed class StoreFake : IRevogacaoDidStore
    {
        public SagaRevogacaoDidDto? Saga { get; set; }
        public IReadOnlyList<SagaRevogacaoDidDto> Pendencias { get; set; } = [];
        public List<string> Acoes { get; } = [];
        public Task<ContextoRevogacaoDid?> ObterContextoAsync(long id, CancellationToken ct = default) => Task.FromResult<ContextoRevogacaoDid?>(new(id, 1, "did:legal:expert:b", "Perito B", true, false));
        public Task<SagaRevogacaoDidDto?> ObterPorCommandIdAsync(string id, CancellationToken ct = default) => Task.FromResult(Saga?.CommandId == id ? Saga : null);
        public Task RegistrarRejeicaoAsync(RejeicaoRevogacaoDid rejeicao, CancellationToken ct = default) { Acoes.Add("REJEICAO"); return Task.CompletedTask; }
        public Task<SagaRevogacaoDidDto> CriarOuObterAsync(NovaSagaRevogacaoDid nova, CancellationToken ct = default)
        {
            Saga ??= new SagaRevogacaoDidDto(nova.CommandId, nova.DidAlvo, nova.CommandJson, nova.KeyId, nova.Signature, EstadoRevogacaoDid.PendenteLedger, "Perito B");
            return Task.FromResult(Saga);
        }
        public Task<SagaRevogacaoDidDto?> TentarAssumirAsync(string id, DateTime agora, TimeSpan lease, CancellationToken ct = default)
        {
            if (Saga?.Estado is EstadoRevogacaoDid.ConfirmadaNoLedger or EstadoRevogacaoDid.FalhaPermanente) return Task.FromResult<SagaRevogacaoDidDto?>(null);
            Saga = Saga! with { Estado = EstadoRevogacaoDid.ProcessandoLedger };
            return Task.FromResult<SagaRevogacaoDidDto?>(Saga);
        }
        public Task<IReadOnlyList<SagaRevogacaoDidDto>> AssumirPendenciasAsync(DateTime agora, TimeSpan lease, int limite, CancellationToken ct = default) => Task.FromResult(Pendencias);
        public Task ConfirmarAsync(string id, DateTime agora, bool reconciliada, CancellationToken ct = default) { Saga = (Saga ?? Pendencias.Single()) with { Estado = EstadoRevogacaoDid.ConfirmadaNoLedger }; Acoes.Add(reconciliada ? "DID_REVOGACAO_RECONCILIADA" : "DID_REVOGACAO_CONFIRMADA"); return Task.CompletedTask; }
        public Task RegistrarFalhaTransitoriaAsync(string id, string erro, DateTime agora, DateTime proxima, CancellationToken ct = default) { Saga = (Saga ?? Pendencias.Single()) with { Estado = EstadoRevogacaoDid.FalhaReconciliacao }; Acoes.Add("DID_REVOGACAO_RECONCILIACAO_FALHOU"); return Task.CompletedTask; }
        public Task RegistrarFalhaDefinitivaAsync(string id, string erro, DateTime agora, CancellationToken ct = default) { Saga = (Saga ?? Pendencias.Single()) with { Estado = EstadoRevogacaoDid.FalhaPermanente }; Acoes.Add("DID_REVOGACAO_REJEITADA"); return Task.CompletedTask; }
    }

    private sealed class LedgerFake : IRevogacaoDidLedger
    {
        public Exception? Falha { get; init; }
        public GovernancaRevogacaoDid Governanca { get; init; } = new(1, ["did:legal:admin:a"]);
        public int Revogacoes { get; private set; }
        public Task<GovernancaRevogacaoDid> ObterGovernancaAsync(CancellationToken ct = default) => Task.FromResult(Governanca);
        public Task RevogarAsync(string did, ProvaRevogacaoDid prova, CancellationToken ct = default) { Revogacoes++; if (Falha is not null) throw Falha; return Task.CompletedTask; }
    }

    private sealed class DidRegistryFake : IDidRegistry
    {
        public DocumentoDidAutenticacao? Documento { get; init; } = new("did:legal:expert:b", 2, "active", true, [], [], 2);
        public Exception? Falha { get; init; }
        public Task<DocumentoDidAutenticacao?> ResolverAsync(string did, CancellationToken ct = default)
        {
            if (Falha is not null) throw Falha;
            return Task.FromResult(Documento);
        }
    }
    private sealed class ClockFake : IClock { public DateTime UtcNow => new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class NonceFake : IGeradorNonce { public byte[] Gerar(int quantidadeBytes) => new byte[quantidadeBytes]; }
}
