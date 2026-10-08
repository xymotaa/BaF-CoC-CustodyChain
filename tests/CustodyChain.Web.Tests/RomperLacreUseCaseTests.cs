using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class RomperLacreUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComProvaConfirmada_PersisteRompimentoAncorado()
    {
        var store = new RompimentoLacreStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(new RomperLacreCommand(3, 17, " Embalagem aberta para exame "));

        var resultado = await useCase.ExecutarAsync(new ConcluirRompimentoLacreCommand(
            3, 17, " Embalagem aberta para exame ", Assinar(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.False(resultado.AncoragemPendente);
        var confirmado = Assert.IsType<RompimentoLacrePendente>(store.RompimentoPersistido);
        Assert.Equal("Embalagem aberta para exame", confirmado.Justificativa);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), confirmado.OperacaoAssinadaId);
        Assert.Equal(confirmado.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersiste()
    {
        var store = new RompimentoLacreStoreFake();
        var useCase = CriarUseCase(store, new LedgerCaptura { Falhar = true });
        var preparacao = await useCase.PrepararAsync(new RomperLacreCommand(3, 17, "Justificativa"));

        await Assert.ThrowsAsync<IndisponibilidadeLedgerRompimentoLacreException>(() => useCase.ExecutarAsync(
            new ConcluirRompimentoLacreCommand(3, 17, "Justificativa", Assinar(preparacao.Operacao))));

        Assert.Null(store.RompimentoPersistido);
    }

    [Fact]
    public async Task PrepararAsync_ComCredencialInvalida_NaoCriaOperacao()
    {
        var store = new RompimentoLacreStoreFake { Contexto = CriarContexto() with { CredencialValida = false } };
        var useCase = CriarUseCase(store, new LedgerCaptura());

        await Assert.ThrowsAsync<CredencialPermissaoInvalidaException>(
            () => useCase.PrepararAsync(new RomperLacreCommand(3, 17, "Justificativa")));
    }

    private static RomperLacreUseCase CriarUseCase(RompimentoLacreStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static ContextoRompimentoLacre CriarContexto() =>
        new(17, 42, "RE-001", "did:legal:expert:teste-001", true, 10,
            "urn:uuid:11111111-1111-1111-1111-111111111111", 9, "L-001");

    private static JsonElement Assinar(JsonElement operacao)
    {
        var node = JsonNode.Parse(operacao.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class RompimentoLacreStoreFake : IRompimentoLacreStore
    {
        public ContextoRompimentoLacre? Contexto { get; init; } = CriarContexto();
        public RompimentoLacrePendente? RompimentoPersistido { get; private set; }

        public Task<ContextoRompimentoLacre?> ObterContextoAsync(
            long periciaId, long peritoId, DateTime agora, CancellationToken cancellationToken) => Task.FromResult(Contexto);

        public Task PersistirAsync(RompimentoLacrePendente rompimento, CancellationToken cancellationToken)
        {
            RompimentoPersistido = rompimento;
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
