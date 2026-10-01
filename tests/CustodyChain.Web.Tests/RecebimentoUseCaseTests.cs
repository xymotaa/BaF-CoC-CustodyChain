using System.Text.Json;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.Recebimento;

namespace CustodyChain.Web.Tests;

public sealed class RecebimentoUseCaseTests
{
    [Fact]
    public async Task ConfirmarAsync_ComLacreIgual_PersisteRecebimentoPendente()
    {
        var store = new RecebimentoStoreFake();
        var useCase = CriarConfirmacao(store);

        var resultado = await useCase.ExecutarAsync(new ConfirmarRecebimentoCommand(2, 15, " LACRE-001 "));

        Assert.True(resultado.LacreConfere);
        var pendente = Assert.IsType<RecebimentoConfirmadoPendente>(store.ConfirmacaoPersistida);
        Assert.Equal("RECEBIMENTO", pendente.Evento);
        Assert.Equal("cred-coc-recebimento", pendente.CredencialId);
        Assert.Equal(64, pendente.PayloadHashSha256.Length);
        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("lacre_conferido", payload.RootElement.GetProperty("Resultado").GetString());
    }

    [Fact]
    public async Task ConfirmarAsync_ComLacreDivergente_RegistraRompimento()
    {
        var store = new RecebimentoStoreFake();
        var useCase = CriarConfirmacao(store);

        var resultado = await useCase.ExecutarAsync(new ConfirmarRecebimentoCommand(2, 15, "LACRE-OUTRO"));

        Assert.False(resultado.LacreConfere);
        var pendente = Assert.IsType<RecebimentoConfirmadoPendente>(store.ConfirmacaoPersistida);
        Assert.Equal("ROMPIMENTO", pendente.Evento);
        Assert.False(pendente.LacreConfere);
    }

    [Fact]
    public async Task RecusarAsync_ComMotivo_PersisteEventoPendente()
    {
        var store = new RecebimentoStoreFake();
        var useCase = CriarRecusa(store);

        var resultado = await useCase.ExecutarAsync(new RecusarRecebimentoCommand(2, 15, " Embalagem danificada "));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        var pendente = Assert.IsType<RecebimentoRecusadoPendente>(store.RecusaPersistida);
        Assert.Equal("Embalagem danificada", pendente.MotivoRecusa);
        Assert.Equal("cred-coc-recebimento", pendente.CredencialId);
        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("Embalagem danificada", payload.RootElement.GetProperty("MotivoRecusa").GetString());
    }

    [Fact]
    public async Task RecusarAsync_SemMotivo_NaoPersiste()
    {
        var store = new RecebimentoStoreFake();
        var useCase = CriarRecusa(store);

        await Assert.ThrowsAsync<ValidacaoRecebimentoException>(
            () => useCase.ExecutarAsync(new RecusarRecebimentoCommand(2, 15, " ")));

        Assert.Null(store.RecusaPersistida);
    }

    private static ConfirmarRecebimentoUseCase CriarConfirmacao(RecebimentoStoreFake store) =>
        new(store, new ClockFixo(), new GeradorCredencialFixo());

    private static RecusarRecebimentoUseCase CriarRecusa(RecebimentoStoreFake store) =>
        new(store, new ClockFixo(), new GeradorCredencialFixo());

    private sealed class RecebimentoStoreFake : IRecebimentoStore
    {
        public RecebimentoConfirmadoPendente? ConfirmacaoPersistida { get; private set; }
        public RecebimentoRecusadoPendente? RecusaPersistida { get; private set; }

        public Task<ContextoRecebimento?> ObterContextoAsync(long movimentacaoId, long destinoId, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoRecebimento?>(new ContextoRecebimento(
                movimentacaoId, 42, "RE-001", "did:legal:custodian:teste-001", "LACRE-001"));

        public Task ConfirmarAsync(RecebimentoConfirmadoPendente recebimento, CancellationToken cancellationToken)
        {
            ConfirmacaoPersistida = recebimento;
            return Task.CompletedTask;
        }

        public Task RecusarAsync(RecebimentoRecusadoPendente recebimento, CancellationToken cancellationToken)
        {
            RecusaPersistida = recebimento;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class GeradorCredencialFixo : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => "cred-coc-recebimento";
    }
}
