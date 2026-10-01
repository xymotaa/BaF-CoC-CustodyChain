using System.Text.Json;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;

namespace CustodyChain.Web.Tests;

public sealed class FracionarAmostraUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_ComDadosValidos_PersisteFracionamentoPendente()
    {
        var store = new FracionamentoAmostraStoreFake();
        var useCase = CriarUseCase(store);

        var resultado = await useCase.ExecutarAsync(new FracionarAmostraCommand(
            3, 17, " RE-002 ", " Fragmento analisado ", "10 g", " Separação técnica "));

        Assert.Equal("RE-001", resultado.RotuloEvidenciaOrigem);
        Assert.Equal("RE-002", resultado.RotuloEvidenciaResultante);
        var pendente = Assert.IsType<FracionamentoAmostraPendente>(store.FracionamentoPersistido);
        Assert.Equal("RE-002", pendente.RotuloEvidenciaResultante);
        Assert.Equal("RC-001", pendente.RotuloConjunto);
        Assert.Equal("cred-coc-fracionamento", pendente.CredencialId);
        Assert.Equal(64, pendente.PayloadHashSha256.Length);
        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("RE-001", payload.RootElement.GetProperty("REOrigem").GetString());
        Assert.Equal("RE-002", payload.RootElement.GetProperty("REResultante").GetString());
    }

    [Fact]
    public async Task ExecutarAsync_ComRotuloExistente_NaoPersiste()
    {
        var store = new FracionamentoAmostraStoreFake { RotuloExiste = true };
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<ConflitoFracionamentoAmostraException>(() => useCase.ExecutarAsync(new FracionarAmostraCommand(
            3, 17, "RE-002", "Fragmento", null, "Separação técnica")));

        Assert.Null(store.FracionamentoPersistido);
    }

    [Fact]
    public async Task ExecutarAsync_SemJustificativa_NaoConsultaNemPersiste()
    {
        var store = new FracionamentoAmostraStoreFake();
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<ValidacaoFracionamentoAmostraException>(() => useCase.ExecutarAsync(new FracionarAmostraCommand(
            3, 17, "RE-002", "Fragmento", null, " ")));

        Assert.False(store.ContextoConsultado);
        Assert.Null(store.FracionamentoPersistido);
    }

    private static FracionarAmostraUseCase CriarUseCase(FracionamentoAmostraStoreFake store) =>
        new(store, new ClockFixo(), new GeradorCredencialFixo());

    private sealed class FracionamentoAmostraStoreFake : IFracionamentoAmostraStore
    {
        public bool RotuloExiste { get; init; }
        public bool ContextoConsultado { get; private set; }
        public FracionamentoAmostraPendente? FracionamentoPersistido { get; private set; }

        public Task<ContextoFracionamentoAmostra?> ObterContextoAsync(
            long periciaId, long peritoId, CancellationToken cancellationToken)
        {
            ContextoConsultado = true;
            return Task.FromResult<ContextoFracionamentoAmostra?>(new ContextoFracionamentoAmostra(
                17, 42, "RE-001", "RC-001", 8, 3, "hash-origem", "did:legal:expert:teste-001"));
        }

        public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) =>
            Task.FromResult(RotuloExiste);

        public Task PersistirAsync(FracionamentoAmostraPendente fracionamento, CancellationToken cancellationToken)
        {
            FracionamentoPersistido = fracionamento;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class GeradorCredencialFixo : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => "cred-coc-fracionamento";
    }
}
