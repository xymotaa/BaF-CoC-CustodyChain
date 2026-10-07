using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class CadastrarVestigioUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComOperacaoConfirmada_PersisteVestigioAncorado()
    {
        var store = new CadastroVestigioStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarUseCase(store, ledger);
        var preparacao = await useCase.PrepararAsync(CriarCommand());

        var resultado = await useCase.ExecutarAsync(
            new ConcluirCadastroVestigioCommand(CriarCommand(), Assinar(preparacao.Operacao)));

        Assert.Equal(42, resultado.VestigioId);
        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), store.CadastroPersistido!.OperacaoAssinadaId);
        Assert.Equal(store.CadastroPersistido.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
        Assert.Equal("did:legal:delegate:teste-001", store.CadastroPersistido.DidResponsavel);
    }

    [Fact]
    public async Task ExecutarAsync_LedgerIndisponivel_NaoPersisteVestigio()
    {
        var store = new CadastroVestigioStoreFake();
        var useCase = CriarUseCase(store, new LedgerCaptura { Falhar = true });
        var preparacao = await useCase.PrepararAsync(CriarCommand());

        await Assert.ThrowsAsync<IndisponibilidadeLedgerCadastroVestigioException>(() =>
            useCase.ExecutarAsync(new ConcluirCadastroVestigioCommand(CriarCommand(), Assinar(preparacao.Operacao))));

        Assert.Null(store.CadastroPersistido);
    }

    [Fact]
    public async Task PrepararAsync_RejeitaRotuloDuplicado()
    {
        var store = new CadastroVestigioStoreFake { RotuloJaExiste = true };
        var useCase = CriarUseCase(store, new LedgerCaptura());

        var exception = await Assert.ThrowsAsync<ConflitoCadastroVestigioException>(() =>
            useCase.PrepararAsync(CriarCommand()));

        Assert.Equal(nameof(CadastrarVestigioCommand.RotuloEvidencia), exception.Campo);
    }

    private static CadastrarVestigioUseCase CriarUseCase(CadastroVestigioStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static CadastrarVestigioCommand CriarCommand() =>
        new(3, " RE-001 ", " RC-001 ", " NE-001 ", 1, 1, " Vestígio de teste ",
            " Local A ", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), " Manual ",
            " L-001 ", false, null);

    private static JsonElement Assinar(JsonElement operacao)
    {
        var node = JsonNode.Parse(operacao.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class CadastroVestigioStoreFake : ICadastroVestigioStore
    {
        public bool RotuloJaExiste { get; init; }
        public CadastroVestigioPendente? CadastroPersistido { get; private set; }
        public Task<bool> RotuloEvidenciaExisteAsync(string rotulo, CancellationToken token) => Task.FromResult(RotuloJaExiste);
        public Task<bool> NumeroLacreExisteAsync(string numero, CancellationToken token) => Task.FromResult(false);
        public Task<ProcessoCadastroVestigio?> ObterProcessoAtivoAsync(long processoId, CancellationToken token) =>
            Task.FromResult<ProcessoCadastroVestigio?>(new(processoId, "0000001-00.2026.8.14.0000"));
        public Task<bool> TipoVestigioExisteAsync(short tipoId, CancellationToken token) => Task.FromResult(true);
        public Task<AtorCadastroVestigio?> ObterAtorAtivoAsync(long intervenienteId, long processoId, DateTime agora, CancellationToken token) =>
            Task.FromResult<AtorCadastroVestigio?>(new(intervenienteId, "did:legal:delegate:teste-001", "urn:uuid:11111111-1111-1111-1111-111111111111"));
        public Task<long> PersistirAsync(CadastroVestigioPendente cadastro, CancellationToken token)
        {
            CadastroPersistido = cadastro;
            return Task.FromResult(42L);
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
