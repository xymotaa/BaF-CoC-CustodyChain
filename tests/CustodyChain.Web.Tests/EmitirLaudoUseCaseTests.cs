using System.Text.Json;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;

namespace CustodyChain.Web.Tests;

public sealed class EmitirLaudoUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComDadosValidos_PersisteLaudoPendente()
    {
        var store = new EmissaoLaudoStoreFake();
        var useCase = CriarUseCase(store);

        var resultado = await useCase.ExecutarAsync(new EmitirLaudoCommand(3, 17, " Conclusão técnica "));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal("LAUDO-2026-000017", resultado.NumeroLaudo);
        var pendente = Assert.IsType<LaudoPendente>(store.LaudoPersistido);
        Assert.Equal("Conclusão técnica", pendente.Conteudo);
        Assert.Equal("hash-vestigio", pendente.HashVestigios);
        Assert.Equal("cred-coc-laudo", pendente.CredencialId);
        Assert.Equal(64, pendente.HashLaudo.Length);
        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("LAUDO-2026-000017", payload.RootElement.GetProperty("NumeroLaudo").GetString());
    }

    [Fact]
    public async Task ExecutarAsync_SemHashDoVestigio_NaoPersiste()
    {
        var store = new EmissaoLaudoStoreFake
        {
            Contexto = CriarContexto() with { HashVestigios = null },
        };
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<HashVestigioAusenteException>(
            () => useCase.ExecutarAsync(new EmitirLaudoCommand(3, 17, "Conclusão técnica")));

        Assert.Null(store.LaudoPersistido);
    }

    [Fact]
    public async Task ExecutarAsync_ConteudoAusente_NaoConsultaNemPersiste()
    {
        var store = new EmissaoLaudoStoreFake();
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<ValidacaoEmissaoLaudoException>(
            () => useCase.ExecutarAsync(new EmitirLaudoCommand(3, 17, " ")));

        Assert.False(store.ContextoConsultado);
        Assert.Null(store.LaudoPersistido);
    }

    private static EmitirLaudoUseCase CriarUseCase(EmissaoLaudoStoreFake store) =>
        new(store, new ClockFixo(), new GeradorCredencialFixo());

    private static ContextoEmissaoLaudo CriarContexto() =>
        new(17, 42, "RE-001", "did:legal:expert:teste-001", "hash-vestigio");

    private sealed class EmissaoLaudoStoreFake : IEmissaoLaudoStore
    {
        public ContextoEmissaoLaudo? Contexto { get; init; } = CriarContexto();
        public bool ContextoConsultado { get; private set; }
        public LaudoPendente? LaudoPersistido { get; private set; }

        public Task<ContextoEmissaoLaudo?> ObterContextoAsync(
            long periciaId, long peritoId, CancellationToken cancellationToken)
        {
            ContextoConsultado = true;
            return Task.FromResult(Contexto);
        }

        public Task PersistirAsync(LaudoPendente laudo, CancellationToken cancellationToken)
        {
            LaudoPersistido = laudo;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class GeradorCredencialFixo : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => "cred-coc-laudo";
    }
}
