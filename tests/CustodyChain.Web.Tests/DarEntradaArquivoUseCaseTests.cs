using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Arquivo;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class DarEntradaArquivoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComProvaConfirmada_PersisteGuardaAncorada()
    {
        var store = new EntradaArquivoStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var command = new DarEntradaArquivoCommand(2, 42, " Central Norte ", " E-12 ", new DateOnly(2027, 1, 15));
        var preparacao = await useCase.PrepararAsync(command);

        var resultado = await useCase.ExecutarAsync(new ConcluirEntradaArquivoCommand(command, Assinar(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal("Central Norte", store.EntradaPersistida!.Central);
        Assert.Equal("E-12", store.EntradaPersistida.Posicao);
        Assert.Equal(store.EntradaPersistida.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersisteGuarda()
    {
        var store = new EntradaArquivoStoreFake();
        var useCase = CriarUseCase(store, new LedgerCaptura { Falhar = true });
        var command = new DarEntradaArquivoCommand(2, 42, "Central Norte", null, null);
        var preparacao = await useCase.PrepararAsync(command);

        await Assert.ThrowsAsync<IndisponibilidadeLedgerEntradaArquivoException>(() =>
            useCase.ExecutarAsync(new ConcluirEntradaArquivoCommand(command, Assinar(preparacao.Operacao))));

        Assert.Null(store.EntradaPersistida);
    }

    [Fact]
    public async Task PrepararAsync_SemCentral_NaoCriaOperacao()
    {
        var useCase = CriarUseCase(new EntradaArquivoStoreFake(), new LedgerCaptura());

        var exception = await Assert.ThrowsAsync<ValidacaoEntradaArquivoException>(() =>
            useCase.PrepararAsync(new DarEntradaArquivoCommand(2, 42, " ", null, null)));

        Assert.Equal(nameof(DarEntradaArquivoCommand.Central), exception.Campo);
    }

    private static DarEntradaArquivoUseCase CriarUseCase(EntradaArquivoStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static JsonElement Assinar(JsonElement operacao)
    {
        var node = JsonNode.Parse(operacao.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class EntradaArquivoStoreFake : IEntradaArquivoStore
    {
        public EntradaArquivoConfirmada? EntradaPersistida { get; private set; }
        public Task<ContextoEntradaArquivo?> ObterContextoAsync(long vestigioId, long recebedorId, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoEntradaArquivo?>(new(42, 10, "urn:uuid:aaaaaaaa-1111-1111-1111-111111111111", "RE-001",
                "did:legal:custodian:teste-001", "urn:uuid:cccccccc-1111-1111-1111-111111111111",
                "urn:uuid:bbbbbbbb-1111-1111-1111-111111111111"));
        public Task PersistirAsync(EntradaArquivoConfirmada entrada, CancellationToken cancellationToken)
        {
            EntradaPersistida = entrada;
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

    private sealed class ClockFixo : IClock { public DateTime UtcNow { get; } = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class NonceFixo : IGeradorNonce { public byte[] Gerar(int tamanho) => Enumerable.Range(0, tamanho).Select(i => (byte)i).ToArray(); }
}
