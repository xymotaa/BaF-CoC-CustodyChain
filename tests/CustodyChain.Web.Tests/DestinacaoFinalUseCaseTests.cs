using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.DestinacaoFinal;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class DestinacaoFinalUseCaseTests
{
    [Fact]
    public async Task Solicitar_AssinadaEConfirmada_PersisteSomenteAposLedger()
    {
        var store = new StoreFake();
        var armazenamento = new ArmazenamentoFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, armazenamento, ledger);
        var entrada = new PrepararSolicitacaoDestinacaoCommand(
            3, 42, "DESCARTE", " did:legal:judge:001 ", " mandado.pdf ", [1, 2, 3], " Motivo ");

        var preparacao = await useCase.PrepararAsync(entrada);
        var resultado = await useCase.ExecutarAsync(new ConcluirSolicitacaoDestinacaoCommand(
            3, 42, Assinar(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal("mandado.pdf", armazenamento.NomeArquivo);
        Assert.Equal("DESTINACAO_SOLICITAR", ledger.OperacaoRecebida!.Value.GetProperty("operation").GetString());
        var solicitacao = Assert.IsType<SolicitacaoDestinacaoPendente>(store.SolicitacaoPersistida);
        Assert.Equal("DESCARTE", solicitacao.Tipo);
        Assert.Equal("did:legal:judge:001", solicitacao.DidMagistrado);
        Assert.Equal("urn:uuid:guarda-1111-1111-1111-111111111111", solicitacao.GuardaOperationId);
        Assert.Equal(64, solicitacao.HashAutorizacao.Length);
    }

    [Fact]
    public async Task Solicitar_QuandoLedgerFalha_NaoPersisteSolicitacao()
    {
        var store = new StoreFake();
        var useCase = CriarUseCase(store, new ArmazenamentoFake(), new LedgerCaptura { Falhar = true });
        var preparacao = await useCase.PrepararAsync(new PrepararSolicitacaoDestinacaoCommand(
            3, 42, "DESCARTE", "did:legal:judge:001", "mandado.pdf", [1, 2, 3], null));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerDestinacaoFinalException>(() => useCase.ExecutarAsync(
            new ConcluirSolicitacaoDestinacaoCommand(3, 42, Assinar(preparacao.Operacao))));

        Assert.Null(store.SolicitacaoPersistida);
    }

    [Fact]
    public async Task Solicitar_ComGuardaDivergente_NaoChamaLedger()
    {
        var store = new StoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, new ArmazenamentoFake(), ledger);
        var preparacao = await useCase.PrepararAsync(new PrepararSolicitacaoDestinacaoCommand(
            3, 42, "DESCARTE", "did:legal:judge:001", "mandado.pdf", [1], null));
        var adulterada = preparacao.Operacao.GetRawText().Replace("guarda-1111", "guarda-9999", StringComparison.Ordinal);

        await Assert.ThrowsAsync<ValidacaoDestinacaoFinalException>(() => useCase.ExecutarAsync(
            new ConcluirSolicitacaoDestinacaoCommand(3, 42, Assinar(JsonDocument.Parse(adulterada).RootElement))));

        Assert.Null(ledger.OperacaoRecebida);
        Assert.Null(store.SolicitacaoPersistida);
    }

    [Fact]
    public async Task Aprovar_AssinadaEConfirmada_PersisteSomenteAposLedger()
    {
        var store = new StoreFake();
        var ledger = new LedgerCaptura();
        var useCase = new AprovarDestinacaoUseCase(store, ledger, new ClockAtual(), new NonceFixo());

        var preparacao = await useCase.PrepararAsync(new PrepararAprovacaoDestinacaoCommand(4, 17));
        var resultado = await useCase.ExecutarAsync(new ConcluirAprovacaoDestinacaoCommand(
            4, 17, Assinar(preparacao.Operacao)));

        Assert.Equal("DESCARTE", resultado.Tipo);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal("DESTINACAO_APROVAR", ledger.OperacaoRecebida!.Value.GetProperty("operation").GetString());
        Assert.Equal("did:legal:admin:teste-001#auth-1", ledger.OperacaoRecebida.Value.GetProperty("keyId").GetString());
        var aprovacao = Assert.IsType<AprovacaoDestinacaoPendente>(store.AprovacaoPersistida);
        Assert.Equal("urn:uuid:solicitacao-1111-1111-1111-111111111111", aprovacao.SolicitacaoOperationId);
    }

    [Fact]
    public async Task Aprovar_QuandoLedgerFalha_NaoPersisteAprovacao()
    {
        var store = new StoreFake();
        var useCase = new AprovarDestinacaoUseCase(store, new LedgerCaptura { Falhar = true }, new ClockAtual(), new NonceFixo());
        var preparacao = await useCase.PrepararAsync(new PrepararAprovacaoDestinacaoCommand(4, 17));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerDestinacaoFinalException>(() => useCase.ExecutarAsync(
            new ConcluirAprovacaoDestinacaoCommand(4, 17, Assinar(preparacao.Operacao))));

        Assert.Null(store.AprovacaoPersistida);
    }

    private static SolicitarDestinacaoUseCase CriarUseCase(StoreFake store, ArmazenamentoFake armazenamento, LedgerCaptura ledger) =>
        new(store, armazenamento, ledger, new ClockAtual(), new NonceFixo());

    private static JsonElement Assinar(JsonElement operacao)
    {
        using var documento = JsonDocument.Parse(operacao.GetRawText());
        var dados = documento.RootElement.EnumerateObject()
            .ToDictionary(propriedade => propriedade.Name, propriedade => propriedade.Value.Clone());
        dados["signature"] = JsonSerializer.SerializeToElement(new string('A', 86));
        return JsonSerializer.SerializeToElement(dados);
    }

    private sealed class StoreFake : IDestinacaoFinalStore
    {
        public SolicitacaoDestinacaoPendente? SolicitacaoPersistida { get; private set; }
        public AprovacaoDestinacaoPendente? AprovacaoPersistida { get; private set; }
        public ContextoAprovacaoDestinacao? ContextoAprovacao { get; init; } = new(
            17, 42, 10, "urn:uuid:asset-1111-1111-1111-111111111111", "RE-001", "DESCARTE",
            "cid-autorizacao", new string('a', 64), "urn:uuid:solicitacao-1111-1111-1111-111111111111",
            "did:legal:custodian:teste-001", "did:legal:admin:teste-001");
        public Task<ContextoSolicitacaoDestinacao?> ObterContextoSolicitacaoAsync(long vestigioId, long solicitanteId, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoSolicitacaoDestinacao?>(new(42, 10, "urn:uuid:asset-1111-1111-1111-111111111111", "RE-001",
                "did:legal:custodian:teste-001", "urn:uuid:cred-1111-1111-1111-111111111111", "urn:uuid:guarda-1111-1111-1111-111111111111"));
        public Task PersistirSolicitacaoAsync(SolicitacaoDestinacaoPendente solicitacao, CancellationToken cancellationToken)
        {
            SolicitacaoPersistida = solicitacao;
            return Task.CompletedTask;
        }
        public Task<ContextoAprovacaoDestinacao?> ObterContextoAprovacaoAsync(long descarteId, long aprovadorId, CancellationToken cancellationToken) => Task.FromResult(ContextoAprovacao);
        public Task PersistirAprovacaoAsync(AprovacaoDestinacaoPendente aprovacao, CancellationToken cancellationToken)
        {
            AprovacaoPersistida = aprovacao;
            return Task.CompletedTask;
        }
    }

    private sealed class ArmazenamentoFake : IArmazenamentoAutorizacao
    {
        public string? NomeArquivo { get; private set; }
        public Task<AutorizacaoArmazenada> ArmazenarAsync(byte[] conteudo, string nomeArquivo, CancellationToken cancellationToken = default)
        {
            NomeArquivo = nomeArquivo;
            return Task.FromResult(new AutorizacaoArmazenada("cid-autorizacao", conteudo.Length));
        }
    }

    private sealed class LedgerCaptura : IServicoLedger
    {
        public bool Falhar { get; init; }
        public JsonElement? OperacaoRecebida { get; private set; }
        public Task<string> RegistrarOperacaoAssinadaV1Async(OperacaoAssinadaV1Dto dto, CancellationToken token = default)
        {
            if (Falhar) throw new InvalidOperationException("Ledger indisponível.");
            OperacaoRecebida = dto.Operation;
            return Task.FromResult(dto.Operation.GetProperty("operationId").GetString()!);
        }
        public Task RegistrarDidV2PendenteAsync(RegistroDidPendenteDto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task AtivarDidV2Async(string did, AtivacaoDidV2Dto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task<string> GerarDidAsync(TipoAtor tipo) => throw new NotSupportedException();
        public Task AtivarDidAsync(string did, string emissor, string senha) => throw new NotSupportedException();
        public Task<DidDocument> ResolverDidAsync(string did) => throw new NotSupportedException();
        public Task<string> EmitirCredencialPermissaoV2Async(CredencialPermissaoV2Dto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task RevogarCredencialV2Async(string credencialId, RevogacaoCredencialV2Dto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ResultadoVerificacao> VerificarCredencialAsync(string credential) => throw new NotSupportedException();
        public Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId) => throw new NotSupportedException();
        public Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credentialId) => throw new NotSupportedException();
    }

    private sealed class ClockAtual : IClock { public DateTime UtcNow => DateTime.UtcNow; }
    private sealed class NonceFixo : IGeradorNonce { public byte[] Gerar(int tamanho) => Enumerable.Range(0, tamanho).Select(i => (byte)i).ToArray(); }
}
