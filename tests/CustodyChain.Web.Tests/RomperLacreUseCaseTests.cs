using System.Text.Json;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;

namespace CustodyChain.Web.Tests;

public sealed class RomperLacreUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComPermissaoELacreIntacto_PersisteRompimentoPendente()
    {
        var store = new RompimentoLacreStoreFake();
        var useCase = CriarUseCase(store);

        var resultado = await useCase.ExecutarAsync(new RomperLacreCommand(3, 17, " Embalagem aberta para exame "));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal("L-001", resultado.NumeroLacre);
        var pendente = Assert.IsType<RompimentoLacrePendente>(store.RompimentoPersistido);
        Assert.Equal("Embalagem aberta para exame", pendente.Justificativa);
        Assert.Equal("cred-coc-rompimento", pendente.CredencialId);
        Assert.Equal(64, pendente.PayloadHashSha256.Length);
        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("L-001", payload.RootElement.GetProperty("NumeroLacreRompido").GetString());
    }

    [Fact]
    public async Task ExecutarAsync_ComCredencialInvalida_NaoPersiste()
    {
        var store = new RompimentoLacreStoreFake
        {
            Contexto = CriarContexto() with { CredencialValida = false },
        };
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<CredencialPermissaoInvalidaException>(
            () => useCase.ExecutarAsync(new RomperLacreCommand(3, 17, "Justificativa")));

        Assert.Null(store.RompimentoPersistido);
    }

    [Fact]
    public async Task ExecutarAsync_SemLacreIntacto_NaoPersiste()
    {
        var store = new RompimentoLacreStoreFake
        {
            Contexto = CriarContexto() with { LacreId = null, NumeroLacre = null },
        };
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<LacreIntactoNaoEncontradoException>(
            () => useCase.ExecutarAsync(new RomperLacreCommand(3, 17, "Justificativa")));

        Assert.Null(store.RompimentoPersistido);
    }

    [Fact]
    public async Task ExecutarAsync_SemJustificativa_NaoConsultaNemPersiste()
    {
        var store = new RompimentoLacreStoreFake();
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<ValidacaoRompimentoLacreException>(
            () => useCase.ExecutarAsync(new RomperLacreCommand(3, 17, " ")));

        Assert.False(store.ContextoConsultado);
        Assert.Null(store.RompimentoPersistido);
    }

    private static RomperLacreUseCase CriarUseCase(RompimentoLacreStoreFake store) =>
        new(store, new ClockFixo(), new GeradorCredencialFixo());

    private static ContextoRompimentoLacre CriarContexto() =>
        new(17, 42, "RE-001", "did:legal:expert:teste-001", true, 9, "L-001");

    private sealed class RompimentoLacreStoreFake : IRompimentoLacreStore
    {
        public ContextoRompimentoLacre? Contexto { get; init; } = CriarContexto();
        public bool ContextoConsultado { get; private set; }
        public RompimentoLacrePendente? RompimentoPersistido { get; private set; }

        public Task<ContextoRompimentoLacre?> ObterContextoAsync(
            long periciaId, long peritoId, DateTime agora, CancellationToken cancellationToken)
        {
            ContextoConsultado = true;
            return Task.FromResult(Contexto);
        }

        public Task PersistirAsync(RompimentoLacrePendente rompimento, CancellationToken cancellationToken)
        {
            RompimentoPersistido = rompimento;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class GeradorCredencialFixo : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => "cred-coc-rompimento";
    }
}
