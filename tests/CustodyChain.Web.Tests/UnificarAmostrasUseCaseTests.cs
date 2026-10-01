using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;

namespace CustodyChain.Web.Tests;

public sealed class UnificarAmostrasUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComOrigensMesmoConjunto_PersisteUnificacaoPendente()
    {
        var store = new StoreFake();
        var useCase = new UnificarAmostrasUseCase(store, new ClockFixo(), new GeradorFixo());

        var resultado = await useCase.ExecutarAsync(new UnificarAmostrasCommand(3, 17, "43", "RE-003", "Item unificado", "Análise conjunta"));

        Assert.Equal(2, resultado.QuantidadeOrigens);
        Assert.Equal("RE-003", Assert.IsType<UnificacaoAmostrasPendente>(store.Pendente).RotuloEvidenciaResultante);
    }

    [Fact]
    public async Task ExecutarAsync_ComConjuntosDiferentes_Rejeita()
    {
        var store = new StoreFake { Origens = [Origem(42, "RC-001"), Origem(43, "RC-002")] };
        var useCase = new UnificarAmostrasUseCase(store, new ClockFixo(), new GeradorFixo());

        await Assert.ThrowsAsync<ValidacaoUnificacaoAmostrasException>(() => useCase.ExecutarAsync(new UnificarAmostrasCommand(3, 17, "43", "RE-003", "Item", "Justificativa")));
        Assert.Null(store.Pendente);
    }

    private static OrigemUnificacaoAmostra Origem(long id, string rc) => new(id, $"RE-{id}", rc, 8, 3, "hash");
    private sealed class StoreFake : IUnificacaoAmostrasStore
    {
        public IReadOnlyList<OrigemUnificacaoAmostra> Origens { get; init; } = [Origem(42, "RC-001"), Origem(43, "RC-001")];
        public UnificacaoAmostrasPendente? Pendente { get; private set; }
        public Task<ContextoUnificacaoAmostras?> ObterContextoAsync(long p, long a, CancellationToken ct) => Task.FromResult<ContextoUnificacaoAmostras?>(new(17, 42, "did:perito"));
        public Task<IReadOnlyList<OrigemUnificacaoAmostra>> ObterOrigensAsync(IReadOnlyList<long> ids, CancellationToken ct) => Task.FromResult(Origens);
        public Task<bool> RotuloEvidenciaExisteAsync(string r, CancellationToken ct) => Task.FromResult(false);
        public Task PersistirAsync(UnificacaoAmostrasPendente u, CancellationToken ct) { Pendente = u; return Task.CompletedTask; }
    }
    private sealed class ClockFixo : IClock { public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class GeradorFixo : IGeradorIdentificadorCredencial { public string GerarCoC() => "cred-coc-unificacao"; }
}
