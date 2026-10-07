using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class UnificarAmostrasUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComVcsDeTodasAsOrigens_PersisteUnificacaoAncorada()
    {
        var store = new StoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(new UnificarAmostrasCommand(
            3, 17, "43", "RE-003", "Item unificado", "Análise conjunta"));

        var resultado = await useCase.ExecutarAsync(new ConcluirUnificacaoAmostrasCommand(
            3, 17, "43", "RE-003", "Item unificado", "Análise conjunta", Assinar(preparacao.Operacao)));

        var pendente = Assert.IsType<UnificacaoAmostrasPendente>(store.Persistida);
        Assert.Equal(2, resultado.QuantidadeOrigens);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), pendente.OperacaoAssinadaId);
        Assert.Equal(pendente.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
        Assert.Equal("cred-42", pendente.Origens[0].CredencialId);
        Assert.Equal("cred-43", pendente.Origens[1].CredencialId);
    }

    [Fact]
    public async Task PrepararAsync_SemVcParaUmaOrigem_Rejeita()
    {
        var store = new StoreFake { Origens = [Origem(42, "RC-001")] };
        var useCase = CriarUseCase(store, new LedgerCaptura());

        await Assert.ThrowsAsync<ValidacaoUnificacaoAmostrasException>(() => useCase.PrepararAsync(
            new UnificarAmostrasCommand(3, 17, "43", "RE-003", "Item", "Justificativa")));

        Assert.Null(store.Persistida);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersiste()
    {
        var store = new StoreFake();
        var useCase = CriarUseCase(store, new LedgerCaptura { Falhar = true });
        var preparacao = await useCase.PrepararAsync(new UnificarAmostrasCommand(
            3, 17, "43", "RE-003", "Item unificado", "Análise conjunta"));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerUnificacaoAmostrasException>(() => useCase.ExecutarAsync(
            new ConcluirUnificacaoAmostrasCommand(3, 17, "43", "RE-003", "Item unificado", "Análise conjunta", Assinar(preparacao.Operacao))));

        Assert.Null(store.Persistida);
    }

    private static UnificarAmostrasUseCase CriarUseCase(StoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static OrigemUnificacaoAmostra Origem(long id, string conjunto) =>
        new(id, $"RE-{id}", conjunto, 8, 3, new string((char)('a' + (int)(id - 42)), 64), $"cred-{id}");

    private static JsonElement Assinar(JsonElement operation)
    {
        var node = JsonNode.Parse(operation.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class StoreFake : IUnificacaoAmostrasStore
    {
        public IReadOnlyList<OrigemUnificacaoAmostra> Origens { get; init; } = [Origem(42, "RC-001"), Origem(43, "RC-001")];
        public UnificacaoAmostrasPendente? Persistida { get; private set; }
        public Task<ContextoUnificacaoAmostras?> ObterContextoAsync(long periciaId, long peritoId, DateTime agora, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoUnificacaoAmostras?>(new(17, 42, "did:legal:expert:teste-001"));
        public Task<IReadOnlyList<OrigemUnificacaoAmostra>> ObterOrigensAsync(IReadOnlyList<long> vestigioIds, long peritoId, DateTime agora, CancellationToken cancellationToken) =>
            Task.FromResult(Origens);
        public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task PersistirAsync(UnificacaoAmostrasPendente unificacao, CancellationToken cancellationToken)
        {
            Persistida = unificacao;
            return Task.CompletedTask;
        }
    }

    private sealed class LedgerCaptura : IServicoLedger
    {
        public bool Falhar { get; init; }
        public string? OperacaoIdRecebida { get; private set; }
        public Task<string> RegistrarOperacaoAssinadaV1Async(OperacaoAssinadaV1Dto dto, CancellationToken cancellationToken = default)
        {
            if (Falhar) throw new InvalidOperationException("Ledger indisponível.");
            OperacaoIdRecebida = dto.Operation.GetProperty("operationId").GetString();
            return Task.FromResult(OperacaoIdRecebida!);
        }
        public Task RegistrarDidV2PendenteAsync(RegistroDidPendenteDto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AtivarDidV2Async(string did, AtivacaoDidV2Dto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GerarDidAsync(TipoAtor tipo) => throw new NotSupportedException();
        public Task AtivarDidAsync(string did, string didEmissor, string senhaEmissor) => throw new NotSupportedException();
        public Task<DidDocument> ResolverDidAsync(string did) => throw new NotSupportedException();
        public Task<string> EmitirCredencialPermissaoV2Async(CredencialPermissaoV2Dto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevogarCredencialV2Async(string credencialId, RevogacaoCredencialV2Dto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson) => throw new NotSupportedException();
        public Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId) => throw new NotSupportedException();
        public Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credencialId) => throw new NotSupportedException();
    }

    private sealed class ClockFixo : IClock { public DateTime UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class NonceFixo : IGeradorNonce { public byte[] Gerar(int quantidadeBytes) => Enumerable.Range(0, quantidadeBytes).Select(item => (byte)item).ToArray(); }
}
