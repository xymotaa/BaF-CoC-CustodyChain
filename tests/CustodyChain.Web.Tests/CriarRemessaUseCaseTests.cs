using System.Text.Json;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.Remessa;

namespace CustodyChain.Web.Tests;

public sealed class CriarRemessaUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_CriaEventoPendenteComCredencialIdempotente()
    {
        var store = new RemessaStoreFake();
        var useCase = CriarUseCase(store);

        var resultado = await useCase.ExecutarAsync(CriarCommand());

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal("Central de Custódia", resultado.NomeDestino);
        Assert.True(resultado.AncoragemPendente);

        var pendente = Assert.IsType<RemessaPendente>(store.RemessaPersistida);
        Assert.Equal("cred-coc-remessa", pendente.CredencialId);
        Assert.Equal("did:legal:delegate:teste-001", pendente.DidResponsavel);
        Assert.Equal(new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), pendente.DataHoraSaida);
        Assert.Equal(64, pendente.PayloadHashSha256.Length);

        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("RE-001", payload.RootElement.GetProperty("RE").GetString());
        Assert.Equal("did:legal:custodian:teste-001", payload.RootElement.GetProperty("Destino").GetString());
    }

    [Fact]
    public async Task ExecutarAsync_RejeitaDestinoIgualAoCriadorSemPersistir()
    {
        var store = new RemessaStoreFake();
        var useCase = CriarUseCase(store);
        var command = CriarCommand() with { DestinoId = 3 };

        var exception = await Assert.ThrowsAsync<ValidacaoRemessaException>(() => useCase.ExecutarAsync(command));

        Assert.Equal(nameof(CriarRemessaCommand.DestinoId), exception.Campo);
        Assert.Null(store.RemessaPersistida);
    }

    [Fact]
    public async Task ExecutarAsync_RejeitaVestigioForaDaCustodiaDoCriador()
    {
        var store = new RemessaStoreFake { Contexto = null };
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<RecursoRemessaNaoEncontradoException>(() => useCase.ExecutarAsync(CriarCommand()));

        Assert.Null(store.RemessaPersistida);
    }

    private static CriarRemessaUseCase CriarUseCase(RemessaStoreFake store) =>
        new(
            store,
            new ClockFixo(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)),
            new GeradorCredencialFixo("cred-coc-remessa"));

    private static CriarRemessaCommand CriarCommand() =>
        new(
            CriadorId: 3,
            VestigioId: 42,
            DestinoId: 2,
            DataHoraSaida: new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            CodigoRastreamento: " RAST-001 ");

    private sealed class RemessaStoreFake : ICriarRemessaStore
    {
        public ContextoRemessa? Contexto { get; init; } = new(
            42,
            "RE-001",
            "did:legal:delegate:teste-001",
            "did:legal:custodian:teste-001",
            "Central de Custódia");

        public RemessaPendente? RemessaPersistida { get; private set; }

        public Task<ContextoRemessa?> ObterContextoAsync(long vestigioId, long criadorId, long destinoId, CancellationToken cancellationToken) =>
            Task.FromResult(Contexto);

        public Task PersistirAsync(RemessaPendente remessa, CancellationToken cancellationToken)
        {
            RemessaPersistida = remessa;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class GeradorCredencialFixo(string identificador) : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => identificador;
    }
}
