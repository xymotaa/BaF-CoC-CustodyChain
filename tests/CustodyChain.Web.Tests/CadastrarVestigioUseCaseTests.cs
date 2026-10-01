using System.Text.Json;
using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Tests;

public sealed class CadastrarVestigioUseCaseTests
{
    [Fact]
    public async Task ExecutarAsync_PersisteVestigioECredencialPendenteNoMesmoComando()
    {
        var store = new CadastroVestigioStoreFake();
        var useCase = new CadastrarVestigioUseCase(
            store,
            new ClockFixo(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)),
            new GeradorCredencialFixo("cred-coc-teste"));

        var resultado = await useCase.ExecutarAsync(CriarCommand());

        Assert.Equal(42, resultado.VestigioId);
        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.True(resultado.AncoragemPendente);

        var pendente = Assert.IsType<CadastroVestigioPendente>(store.CadastroPersistido);
        Assert.Equal("cred-coc-teste", pendente.CredencialId);
        Assert.Equal("did:legal:delegate:teste-001", pendente.DidResponsavel);
        Assert.Equal(64, pendente.PayloadHashSha256.Length);

        using var payload = JsonDocument.Parse(pendente.PayloadJson);
        Assert.Equal("0000001-00.2026.8.14.0000", payload.RootElement.GetProperty("NC").GetString());
        Assert.Equal("RE-001", payload.RootElement.GetProperty("RE").GetString());
        Assert.Equal("did:legal:delegate:teste-001", payload.RootElement.GetProperty("CRI").GetString());
    }

    [Fact]
    public async Task ExecutarAsync_RejeitaRotuloDuplicadoSemPersistir()
    {
        var store = new CadastroVestigioStoreFake { RotuloJaExiste = true };
        var useCase = CriarUseCase(store);

        var exception = await Assert.ThrowsAsync<ConflitoCadastroVestigioException>(() => useCase.ExecutarAsync(CriarCommand()));

        Assert.Equal(nameof(CadastrarVestigioCommand.RotuloEvidencia), exception.Campo);
        Assert.Null(store.CadastroPersistido);
    }

    [Fact]
    public async Task ExecutarAsync_RejeitaIntercorrenciaSemDescricao()
    {
        var store = new CadastroVestigioStoreFake();
        var useCase = CriarUseCase(store);
        var command = CriarCommand() with { HouveIntercorrencia = true, DescricaoIntercorrencia = "  " };

        var exception = await Assert.ThrowsAsync<ValidacaoCadastroVestigioException>(() => useCase.ExecutarAsync(command));

        Assert.Equal(nameof(CadastrarVestigioCommand.DescricaoIntercorrencia), exception.Campo);
        Assert.Null(store.CadastroPersistido);
    }

    private static CadastrarVestigioUseCase CriarUseCase(CadastroVestigioStoreFake store) =>
        new(store, new ClockFixo(DateTime.UnixEpoch), new GeradorCredencialFixo("cred-coc-teste"));

    private static CadastrarVestigioCommand CriarCommand() =>
        new(
            CriadorId: 3,
            RotuloEvidencia: " RE-001 ",
            RotuloConjunto: " RC-001 ",
            NumeroEvidencia: " NE-001 ",
            ProcessoId: 1,
            TipoVestigioId: 1,
            Descricao: " Vestígio de teste ",
            LocalColeta: " Local A ",
            DataHoraColeta: new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
            MetodoColeta: " Manual ",
            NumeroLacre: " L-001 ",
            HouveIntercorrencia: false,
            DescricaoIntercorrencia: null);

    private sealed class CadastroVestigioStoreFake : ICadastroVestigioStore
    {
        public bool RotuloJaExiste { get; init; }
        public CadastroVestigioPendente? CadastroPersistido { get; private set; }

        public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) => Task.FromResult(RotuloJaExiste);
        public Task<bool> NumeroLacreExisteAsync(string numeroLacre, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<ProcessoCadastroVestigio?> ObterProcessoAtivoAsync(long processoId, CancellationToken cancellationToken) => Task.FromResult<ProcessoCadastroVestigio?>(new(processoId, "0000001-00.2026.8.14.0000"));
        public Task<bool> TipoVestigioExisteAsync(short tipoVestigioId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<AtorCadastroVestigio?> ObterAtorAtivoAsync(long intervenienteId, CancellationToken cancellationToken) => Task.FromResult<AtorCadastroVestigio?>(new(intervenienteId, "did:legal:delegate:teste-001"));

        public Task<long> PersistirAsync(CadastroVestigioPendente cadastro, CancellationToken cancellationToken)
        {
            CadastroPersistido = cadastro;
            return Task.FromResult(42L);
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
