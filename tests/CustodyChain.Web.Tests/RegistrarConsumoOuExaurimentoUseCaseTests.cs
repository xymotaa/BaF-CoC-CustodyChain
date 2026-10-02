using System.Text.Json;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.ProcessamentoPericial;

namespace CustodyChain.Web.Tests;

public sealed class RegistrarConsumoOuExaurimentoUseCaseTests
{
    [Theory]
    [InlineData("CONSUMO")]
    [InlineData("EXAURIMENTO")]
    public async Task ExecutarAsync_ComDadosValidos_PersisteOperacaoComAncoragemPendente(string tipo)
    {
        var store = new StoreFake();
        var useCase = CriarUseCase(store);

        var resultado = await useCase.ExecutarAsync(new RegistrarConsumoOuExaurimentoCommand(
            3, 17, tipo, " 10 g ", " Análise técnica "));

        Assert.Equal(tipo, resultado.Tipo);
        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        var pendente = Assert.IsType<ConsumoOuExaurimentoPendente>(store.OperacaoPendente);
        Assert.Equal(tipo, pendente.Tipo);
        Assert.Equal("10 g", pendente.QuantidadeDescrita);
        Assert.Equal("cred-coc-operacao", pendente.CredencialId);
        Assert.Equal(64, pendente.PayloadHashSha256.Length);
        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("RE-001", payload.RootElement.GetProperty("RE").GetString());
        Assert.Equal("Análise técnica", payload.RootElement.GetProperty("Justificativa").GetString());
    }

    [Theory]
    [InlineData("FRACIONAMENTO", "Justificativa")]
    [InlineData("CONSUMO", " ")]
    public async Task ExecutarAsync_ComDadosInvalidos_NaoConsultaNemPersiste(string tipo, string justificativa)
    {
        var store = new StoreFake();
        var useCase = CriarUseCase(store);

        await Assert.ThrowsAsync<ValidacaoConsumoOuExaurimentoException>(() => useCase.ExecutarAsync(
            new RegistrarConsumoOuExaurimentoCommand(3, 17, tipo, null, justificativa)));

        Assert.False(store.ContextoConsultado);
        Assert.Null(store.OperacaoPendente);
    }

    private static RegistrarConsumoOuExaurimentoUseCase CriarUseCase(StoreFake store) =>
        new(store, new ClockFixo(), new GeradorCredencialFixo());

    private sealed class StoreFake : IConsumoOuExaurimentoStore
    {
        public bool ContextoConsultado { get; private set; }
        public ConsumoOuExaurimentoPendente? OperacaoPendente { get; private set; }

        public Task<ContextoConsumoOuExaurimento?> ObterContextoAsync(
            long periciaId,
            long peritoId,
            CancellationToken cancellationToken)
        {
            ContextoConsultado = true;
            return Task.FromResult<ContextoConsumoOuExaurimento?>(
                new ContextoConsumoOuExaurimento(17, 42, "RE-001", "did:legal:expert:teste-001"));
        }

        public Task PersistirAsync(ConsumoOuExaurimentoPendente operacao, CancellationToken cancellationToken)
        {
            OperacaoPendente = operacao;
            return Task.CompletedTask;
        }
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class GeradorCredencialFixo : IGeradorIdentificadorCredencial
    {
        public string GerarCoC() => "cred-coc-operacao";
    }
}
