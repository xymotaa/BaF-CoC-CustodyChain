using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class EmitirLaudoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComOperacaoConfirmada_PersisteLaudoERegistroAncorado()
    {
        var store = new EmissaoLaudoStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(new EmitirLaudoCommand(3, 17, " Conclusão técnica "));

        var resultado = await useCase.ExecutarAsync(new ConcluirEmissaoLaudoCommand(
            3, 17, " Conclusão técnica ", AssinarParaTeste(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal("LAUDO-2026-000017", resultado.NumeroLaudo);
        var pendente = Assert.IsType<LaudoPendente>(store.LaudoPersistido);
        Assert.Equal("Conclusão técnica", pendente.Conteudo);
        Assert.Equal("hash-vestigio", pendente.HashVestigios);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), pendente.OperacaoAssinadaId);
        Assert.Equal(64, pendente.OperacaoAssinadaHashSha256.Length);
        Assert.Equal(pendente.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersisteLaudo()
    {
        var store = new EmissaoLaudoStoreFake();
        var ledger = new LedgerCaptura { Falhar = true };
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(new EmitirLaudoCommand(3, 17, "Conclusão técnica"));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerEmissaoLaudoException>(() => useCase.ExecutarAsync(
            new ConcluirEmissaoLaudoCommand(3, 17, "Conclusão técnica", AssinarParaTeste(preparacao.Operacao))));

        Assert.Null(store.LaudoPersistido);
    }

    [Fact]
    public async Task ExecutarAsync_OperacaoComHashDiferente_NaoPersiste()
    {
        var store = new EmissaoLaudoStoreFake();
        var useCase = CriarUseCase(store, new LedgerCaptura());
        var preparacao = await useCase.PrepararAsync(new EmitirLaudoCommand(3, 17, "Conclusão técnica"));
        var operacao = JsonNode.Parse(preparacao.Operacao.GetRawText())!.AsObject();
        operacao["payload"]!["hashLaudo"] = new string('a', 64);

        await Assert.ThrowsAsync<ValidacaoEmissaoLaudoException>(() => useCase.ExecutarAsync(
            new ConcluirEmissaoLaudoCommand(3, 17, "Conclusão técnica", AssinarParaTeste(operacao))));

        Assert.Null(store.LaudoPersistido);
    }

    [Fact]
    public async Task PrepararAsync_SemHashDoVestigio_NaoPersiste()
    {
        var store = new EmissaoLaudoStoreFake { Contexto = CriarContexto() with { HashVestigios = null } };
        var useCase = CriarUseCase(store, new LedgerCaptura());

        await Assert.ThrowsAsync<HashVestigioAusenteException>(
            () => useCase.PrepararAsync(new EmitirLaudoCommand(3, 17, "Conclusão técnica")));

        Assert.Null(store.LaudoPersistido);
    }

    private static EmitirLaudoUseCase CriarUseCase(EmissaoLaudoStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static ContextoEmissaoLaudo CriarContexto() =>
        new(17, 42, 10, "RE-001", "did:legal:expert:teste-001",
            "urn:uuid:11111111-1111-1111-1111-111111111111", "hash-vestigio");

    private static JsonElement AssinarParaTeste(JsonElement operacao) =>
        AssinarParaTeste(JsonNode.Parse(operacao.GetRawText())!);

    private static JsonElement AssinarParaTeste(JsonNode operacao)
    {
        operacao.AsObject()["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(operacao);
    }

    private sealed class EmissaoLaudoStoreFake : IEmissaoLaudoStore
    {
        public ContextoEmissaoLaudo? Contexto { get; init; } = CriarContexto();
        public LaudoPendente? LaudoPersistido { get; private set; }

        public Task<ContextoEmissaoLaudo?> ObterContextoAsync(long periciaId, long peritoId, CancellationToken cancellationToken) =>
            Task.FromResult(Contexto);

        public Task PersistirAsync(LaudoPendente laudo, CancellationToken cancellationToken)
        {
            LaudoPersistido = laudo;
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

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class NonceFixo : IGeradorNonce
    {
        public byte[] Gerar(int quantidadeBytes) => Enumerable.Range(0, quantidadeBytes).Select(item => (byte)item).ToArray();
    }
}
