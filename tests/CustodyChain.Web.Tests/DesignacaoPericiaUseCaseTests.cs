using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.Pericias;
using CustodyChain.Web.Application.VerifiableCredentials;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class DesignacaoPericiaUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Solicitar_ComDadosValidos_PersistePedidoSemCredencial()
    {
        var store = new StoreFake();
        var useCase = new SolicitarDesignacaoPericiaUseCase(store, new ClockFixo());

        var resultado = await useCase.ExecutarAsync(new SolicitarDesignacaoPericiaCommand(
            3, 42, 7, "  Genética  ", "urgente"));

        Assert.Equal(17, resultado.PericiaId);
        var solicitacao = Assert.IsType<SolicitacaoDesignacaoPericiaPendente>(store.Solicitacao);
        Assert.Equal(3, solicitacao.SolicitanteId);
        Assert.Equal("Genética", solicitacao.AreaPericial);
        Assert.Equal("URGENTE", solicitacao.Prioridade);
    }

    [Fact]
    public async Task Preparar_CriaVcDeterministicaComEscopoCompleto()
    {
        var store = new StoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);

        var resultado = await useCase.PrepararAsync(
            new PrepararAprovacaoDesignacaoPericiaCommand(17, 1, Agora.AddDays(1)));

        var credential = resultado.Credencial;
        var authorization = credential.GetProperty("credentialSubject").GetProperty("authorization");
        Assert.Equal(StoreFake.Identificador, credential.GetProperty("id").GetString());
        Assert.Equal("10", authorization.GetProperty("processoId").GetString());
        Assert.Equal("42", authorization.GetProperty("assetId").GetString());
        Assert.Equal(AprovarDesignacaoPericiaUseCase.OperacoesAutorizadas,
            authorization.GetProperty("operations").EnumerateArray().Select(item => item.GetString()));
        Assert.False(credential.TryGetProperty("proof", out _));
    }

    [Fact]
    public async Task Concluir_ComVcCorrespondente_ConfirmaLedgerAntesDoEstadoLocal()
    {
        var store = new StoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(
            new PrepararAprovacaoDesignacaoPericiaCommand(17, 1, Agora.AddDays(1)));

        await useCase.ConcluirAsync(new ConcluirAprovacaoDesignacaoPericiaCommand(
            17, 1, Assinar(preparacao.Credencial)));

        Assert.Equal(StoreFake.Identificador, ledger.IdentificadorRecebido);
        Assert.True(store.Concluida);
    }

    [Fact]
    public async Task Concluir_QuandoLedgerFalha_NaoAlteraEstadoLocal()
    {
        var store = new StoreFake();
        var ledger = new LedgerCaptura { Falhar = true };
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(
            new PrepararAprovacaoDesignacaoPericiaCommand(17, 1, Agora.AddDays(1)));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerDesignacaoPericiaException>(() =>
            useCase.ConcluirAsync(new ConcluirAprovacaoDesignacaoPericiaCommand(
                17, 1, Assinar(preparacao.Credencial))));

        Assert.False(store.Concluida);
    }

    private static AprovarDesignacaoPericiaUseCase CriarUseCase(StoreFake store, IServicoLedger ledger) =>
        new(store, new CriarVcPermissao(new ClockFixo()), ledger, new ClockFixo());

    private static JsonElement Assinar(JsonElement credential)
    {
        var node = JsonNode.Parse(credential.GetRawText())!.AsObject();
        node["proof"] = new JsonObject
        {
            ["type"] = "CustodyChainEd25519Signature2026",
            ["created"] = Agora.ToString("O"),
            ["proofPurpose"] = "assertionMethod",
            ["verificationMethod"] = "did:legal:admin:emissor#key-1",
            ["canonicalization"] = "custodychain-json-c14n-v1",
            ["proofValue"] = "assinatura"
        };
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class StoreFake : IDesignacaoPericiaStore
    {
        public const string Identificador = "urn:uuid:11111111-1111-1111-1111-111111111111";
        public SolicitacaoDesignacaoPericiaPendente? Solicitacao { get; private set; }
        public bool Concluida { get; private set; }
        private DateTime? _validaAte;

        public Task<ContextoSolicitacaoDesignacaoPericia?> ObterContextoSolicitacaoAsync(
            long vestigioId, long peritoId, long solicitanteId, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoSolicitacaoDesignacaoPericia?>(new(42, 10, "RE-001", 7, "Perita Teste"));

        public Task<long> PersistirSolicitacaoAsync(
            SolicitacaoDesignacaoPericiaPendente solicitacao, CancellationToken cancellationToken)
        {
            Solicitacao = solicitacao;
            return Task.FromResult(17L);
        }

        public Task<IReadOnlyList<SolicitacaoDesignacaoPericiaResumo>> ListarPendentesAsync(
            long aprovadorId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SolicitacaoDesignacaoPericiaResumo>>([]);

        public Task<SolicitacaoDesignacaoPericiaDetalhe?> ObterDetalheAsync(
            long periciaId, long aprovadorId, CancellationToken cancellationToken) =>
            Task.FromResult<SolicitacaoDesignacaoPericiaDetalhe?>(new(
                17, "RE-001", "Amostra", "Perita Teste", "did:legal:expert:titular",
                "Genética", "NORMAL", Agora));

        public Task<ContextoAprovacaoDesignacaoPericia?> PrepararAprovacaoAsync(
            PreparacaoCredencialDesignacaoPericia preparacao, CancellationToken cancellationToken)
        {
            _validaAte ??= preparacao.ValidaAte;
            return Task.FromResult<ContextoAprovacaoDesignacaoPericia?>(CriarContexto());
        }

        public Task<ContextoAprovacaoDesignacaoPericia?> ObterContextoAprovacaoAsync(
            long periciaId, long aprovadorId, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoAprovacaoDesignacaoPericia?>(CriarContexto());

        public Task ConcluirAprovacaoAsync(long periciaId, long aprovadorId,
            string identificadorCredencial, DateTime concluidaEm, CancellationToken cancellationToken)
        {
            Concluida = true;
            return Task.CompletedTask;
        }

        private ContextoAprovacaoDesignacaoPericia CriarContexto() => new(
            17, Identificador, Agora, _validaAte, "did:legal:admin:emissor",
            "did:legal:expert:titular", 10, 42, "RE-001", "Perita Teste", Concluida);
    }

    private sealed class LedgerCaptura : IServicoLedger
    {
        public bool Falhar { get; init; }
        public string? IdentificadorRecebido { get; private set; }

        public Task<string> EmitirCredencialPermissaoV2Async(
            CredencialPermissaoV2Dto dto, CancellationToken cancellationToken = default)
        {
            if (Falhar) throw new InvalidOperationException("Ledger indisponível.");
            IdentificadorRecebido = dto.Credential.GetProperty("id").GetString();
            return Task.FromResult(IdentificadorRecebido!);
        }

        public Task RegistrarDidV2PendenteAsync(RegistroDidPendenteDto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AtivarDidV2Async(string did, AtivacaoDidV2Dto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GerarDidAsync(TipoAtor tipo) => throw new NotSupportedException();
        public Task AtivarDidAsync(string did, string didEmissor, string senhaEmissor) => throw new NotSupportedException();
        public Task<DidDocument> ResolverDidAsync(string did) => throw new NotSupportedException();
        public Task RevogarCredencialV2Async(string credencialId, RevogacaoCredencialV2Dto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson) => throw new NotSupportedException();
        public Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId) => throw new NotSupportedException();
        public Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credencialId) => throw new NotSupportedException();
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow => Agora;
    }
}
