using System.Text.Json;
using CustodyChain.Web.Application.Arquivo;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Tests;

public sealed class DarEntradaArquivoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComDadosValidos_PersisteGuardaPendente()
    {
        var store = new EntradaArquivoStoreFake();
        var useCase = CriarUseCase(store);

        var resultado = await useCase.ExecutarAsync(new DarEntradaArquivoCommand(
            2, 42, " Central Norte ", " E-12 ", new DateOnly(2027, 1, 15)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.True(resultado.AncoragemPendente);
        var pendente = Assert.IsType<EntradaArquivoPendente>(store.EntradaPersistida);
        Assert.Equal("Central Norte", pendente.Central);
        Assert.Equal("E-12", pendente.Posicao);
        Assert.Equal("cred-coc-guarda", pendente.CredencialId);
        Assert.Equal(64, pendente.PayloadHashSha256.Length);
        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("RE-001", payload.RootElement.GetProperty("RE").GetString());
        Assert.Equal("did:legal:custodian:teste-001", payload.RootElement.GetProperty("Responsavel").GetString());
    }

    [Fact]
    public async Task ExecutarAsync_QuandoVestigioNaoEstaRecebido_NaoPersiste()
    {
        var store = new EntradaArquivoStoreFake { Contexto = null };
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<RecursoEntradaArquivoNaoEncontradoException>(
            () => useCase.ExecutarAsync(new DarEntradaArquivoCommand(2, 42, "Central Norte", null, null)));

        Assert.Null(store.EntradaPersistida);
    }

    [Fact]
    public async Task ExecutarAsync_SemCentral_NaoPersiste()
    {
        var store = new EntradaArquivoStoreFake();
        var useCase = CriarUseCase(store);

        var exception = await Assert.ThrowsAsync<ValidacaoEntradaArquivoException>(
            () => useCase.ExecutarAsync(new DarEntradaArquivoCommand(2, 42, " ", null, null)));

        Assert.Equal(nameof(DarEntradaArquivoCommand.Central), exception.Campo);
        Assert.Null(store.EntradaPersistida);
    }

    private static DarEntradaArquivoUseCase CriarUseCase(EntradaArquivoStoreFake store) =>
        new(store, new ClockFixo(), new GeradorCredencialFixo());

    private sealed class EntradaArquivoStoreFake : IEntradaArquivoStore
    {
        public ContextoEntradaArquivo? Contexto { get; init; } = new(
            42, "RE-001", "did:legal:custodian:teste-001");
        public EntradaArquivoPendente? EntradaPersistida { get; private set; }

        public Task<ContextoEntradaArquivo?> ObterContextoAsync(
            long vestigioId, long recebedorId, CancellationToken cancellationToken) => Task.FromResult(Contexto);

        public Task PersistirAsync(EntradaArquivoPendente entrada, CancellationToken cancellationToken)
        {
            EntradaPersistida = entrada;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class GeradorCredencialFixo : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => "cred-coc-guarda";
    }
}
