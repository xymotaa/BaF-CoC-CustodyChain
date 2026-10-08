using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class FracionarAmostraUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComProvaConfirmada_PersisteFracionamentoAncorado()
    {
        var store = new FracionamentoAmostraStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(new FracionarAmostraCommand(
            3, 17, " RE-002 ", " Fragmento analisado ", "10 g", " Separação técnica "));

        var resultado = await useCase.ExecutarAsync(new ConcluirFracionamentoAmostraCommand(
            3, 17, " RE-002 ", " Fragmento analisado ", "10 g", " Separação técnica ", Assinar(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidenciaOrigem);
        Assert.Equal("RE-002", resultado.RotuloEvidenciaResultante);
        var pendente = Assert.IsType<FracionamentoAmostraPendente>(store.FracionamentoPersistido);
        Assert.Equal("RE-002", pendente.RotuloEvidenciaResultante);
        Assert.Equal("RC-001", pendente.RotuloConjunto);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), pendente.OperacaoAssinadaId);
        Assert.Equal(pendente.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task PrepararAsync_ComRotuloExistente_NaoCriaOperacao()
    {
        var store = new FracionamentoAmostraStoreFake { RotuloExiste = true };
        var useCase = CriarUseCase(store, new LedgerCaptura());

        await Assert.ThrowsAsync<ConflitoFracionamentoAmostraException>(() => useCase.PrepararAsync(new FracionarAmostraCommand(
            3, 17, "RE-002", "Fragmento", null, "Separação técnica")));

        Assert.Null(store.FracionamentoPersistido);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersiste()
    {
        var store = new FracionamentoAmostraStoreFake();
        var useCase = CriarUseCase(store, new LedgerCaptura { Falhar = true });
        var preparacao = await useCase.PrepararAsync(new FracionarAmostraCommand(
            3, 17, "RE-002", "Fragmento", null, "Separação técnica"));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerFracionamentoAmostraException>(() => useCase.ExecutarAsync(
            new ConcluirFracionamentoAmostraCommand(3, 17, "RE-002", "Fragmento", null, "Separação técnica", Assinar(preparacao.Operacao))));

        Assert.Null(store.FracionamentoPersistido);
    }

    private static FracionarAmostraUseCase CriarUseCase(FracionamentoAmostraStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static JsonElement Assinar(JsonElement operation)
    {
        var node = JsonNode.Parse(operation.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class FracionamentoAmostraStoreFake : IFracionamentoAmostraStore
    {
        public bool RotuloExiste { get; init; }
        public bool ContextoConsultado { get; private set; }
        public FracionamentoAmostraPendente? FracionamentoPersistido { get; private set; }

        public Task<ContextoFracionamentoAmostra?> ObterContextoAsync(
            long periciaId, long peritoId, DateTime agora, CancellationToken cancellationToken)
        {
            ContextoConsultado = true;
            return Task.FromResult<ContextoFracionamentoAmostra?>(new ContextoFracionamentoAmostra(
                17, 42, "RE-001", "RC-001", 8, 3, new string('a', 64), "did:legal:expert:teste-001", "urn:uuid:11111111-1111-1111-1111-111111111111"));
        }

        public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) =>
            Task.FromResult(RotuloExiste);

        public Task PersistirAsync(FracionamentoAmostraPendente fracionamento, CancellationToken cancellationToken)
        {
            FracionamentoPersistido = fracionamento;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class NonceFixo : IGeradorNonce
    {
        public byte[] Gerar(int quantidadeBytes) => Enumerable.Range(0, quantidadeBytes).Select(item => (byte)item).ToArray();
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
}
