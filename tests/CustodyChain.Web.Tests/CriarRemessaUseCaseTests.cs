using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class CriarRemessaUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComProvaConfirmada_PersisteRemessaInicialAncorada()
    {
        var store = new RemessaStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(CriarCommand());

        var resultado = await useCase.ExecutarAsync(
            new ConcluirRemessaCommand(CriarCommand(), Assinar(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal("Central de Custódia", resultado.NomeDestino);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), store.RemessaPersistida!.OperacaoAssinadaId);
        Assert.Equal(store.RemessaPersistida.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersisteRemessa()
    {
        var store = new RemessaStoreFake();
        var useCase = CriarUseCase(store, new LedgerCaptura { Falhar = true });
        var preparacao = await useCase.PrepararAsync(CriarCommand());

        await Assert.ThrowsAsync<IndisponibilidadeLedgerRemessaException>(() =>
            useCase.ExecutarAsync(new ConcluirRemessaCommand(CriarCommand(), Assinar(preparacao.Operacao))));

        Assert.Null(store.RemessaPersistida);
    }

    [Fact]
    public async Task PrepararAsync_RejeitaDestinoIgualAoCriador()
    {
        var useCase = CriarUseCase(new RemessaStoreFake(), new LedgerCaptura());
        var exception = await Assert.ThrowsAsync<ValidacaoRemessaException>(() =>
            useCase.PrepararAsync(CriarCommand() with { DestinoId = 3 }));

        Assert.Equal(nameof(CriarRemessaCommand.DestinoId), exception.Campo);
    }

    private static CriarRemessaUseCase CriarUseCase(RemessaStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static CriarRemessaCommand CriarCommand() =>
        new(3, 42, 2, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), " RAST-001 ");

    private static JsonElement Assinar(JsonElement operacao)
    {
        var node = JsonNode.Parse(operacao.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class RemessaStoreFake : ICriarRemessaStore
    {
        public RemessaConfirmada? RemessaPersistida { get; private set; }
        public Task<ContextoRemessa?> ObterContextoAsync(long vestigioId, long criadorId, long destinoId, CancellationToken token) =>
            Task.FromResult<ContextoRemessa?>(new(
                42, "urn:uuid:aaaaaaaa-1111-1111-1111-111111111111", 10, "RE-001",
                "did:legal:delegate:teste-001", "did:legal:custodian:teste-001", "Central de Custódia",
                "urn:uuid:bbbbbbbb-1111-1111-1111-111111111111"));
        public Task PersistirAsync(RemessaConfirmada remessa, CancellationToken token)
        {
            RemessaPersistida = remessa;
            return Task.CompletedTask;
        }
    }

    private sealed class LedgerCaptura : IServicoLedger
    {
        public bool Falhar { get; init; }
        public string? OperacaoIdRecebida { get; private set; }
        public Task<string> RegistrarOperacaoAssinadaV1Async(OperacaoAssinadaV1Dto dto, CancellationToken token = default)
        {
            if (Falhar) throw new InvalidOperationException("Ledger indisponível.");
            OperacaoIdRecebida = dto.Operation.GetProperty("operationId").GetString();
            return Task.FromResult(OperacaoIdRecebida!);
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

    private sealed class ClockFixo : IClock { public DateTime UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class NonceFixo : IGeradorNonce { public byte[] Gerar(int tamanho) => Enumerable.Range(0, tamanho).Select(i => (byte)i).ToArray(); }
}
