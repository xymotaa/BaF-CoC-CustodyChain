using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class ReceberPericiaUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComProvaConfirmada_PersisteRecebimento()
    {
        var store = new RecebimentoStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = new ReceberPericiaUseCase(store, ledger, new ClockFixo(), new NonceFixo());
        var preparacao = await useCase.PrepararAsync(new ReceberPericiaCommand(3, 17));

        var resultado = await useCase.ExecutarAsync(new ConcluirRecebimentoPericiaCommand(3, 17, Assinar(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), store.Confirmado!.OperacaoAssinadaId);
        Assert.Equal(store.Confirmado.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersiste()
    {
        var store = new RecebimentoStoreFake();
        var useCase = new ReceberPericiaUseCase(store, new LedgerCaptura { Falhar = true }, new ClockFixo(), new NonceFixo());
        var preparacao = await useCase.PrepararAsync(new ReceberPericiaCommand(3, 17));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerRecebimentoPericiaException>(() =>
            useCase.ExecutarAsync(new ConcluirRecebimentoPericiaCommand(3, 17, Assinar(preparacao.Operacao))));

        Assert.Null(store.Confirmado);
    }

    private static JsonElement Assinar(JsonElement operacao)
    {
        var node = JsonNode.Parse(operacao.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class RecebimentoStoreFake : IRecebimentoPericiaStore
    {
        public RecebimentoPericiaConfirmado? Confirmado { get; private set; }
        public Task<ContextoRecebimentoPericia?> ObterContextoAsync(long periciaId, long peritoId, DateTime agora, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoRecebimentoPericia?>(new(17, 42, 10, "RE-001", "did:legal:expert:teste-001", "urn:uuid:11111111-1111-1111-1111-111111111111"));
        public Task PersistirAsync(RecebimentoPericiaConfirmado recebimento, CancellationToken cancellationToken)
        {
            Confirmado = recebimento;
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
        public Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson) => throw new NotSupportedException();
        public Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId) => throw new NotSupportedException();
        public Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credencialId) => throw new NotSupportedException();
    }

    private sealed class ClockFixo : IClock { public DateTime UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class NonceFixo : IGeradorNonce { public byte[] Gerar(int quantidadeBytes) => Enumerable.Range(0, quantidadeBytes).Select(item => (byte)item).ToArray(); }
}
