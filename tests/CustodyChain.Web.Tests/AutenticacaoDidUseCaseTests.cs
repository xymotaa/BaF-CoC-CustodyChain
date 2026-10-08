using System.Text;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Services.Autenticacao;

namespace CustodyChain.Web.Tests;

public class AutenticacaoDidUseCaseTests
{
    private const string Did = "did:legal:admin:teste-unitario";
    private const string KeyId = $"{Did}#auth-1";
    private static readonly DateTime Agora = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CriarDesafio_ParaDidV2Ativo_ArmazenaConteudoCanonico()
    {
        var store = new DesafioStoreFake();
        var sut = new CriarDesafioAutenticacaoUseCase(
            new DidRegistryFake(DocumentoAtivo()),
            store,
            new GeradorNonceFake(),
            new ClockFake(Agora),
            new ConfiguracaoAutenticacaoDid("custodychain-web", TimeSpan.FromMinutes(2)));

        var resultado = await sut.ExecutarAsync(Did);

        Assert.Equal(Did, resultado.Did);
        Assert.Equal(KeyId, resultado.KeyId);
        Assert.Equal(Agora.AddMinutes(2), resultado.ExpiresAt);
        Assert.NotNull(store.Armazenado);
        var conteudo = Encoding.UTF8.GetString(store.Armazenado!.SigningInput);
        Assert.Contains("custodychain-auth-v1", conteudo);
        Assert.Contains($"did:{Did}", conteudo);
        Assert.Contains("audience:custodychain-web", conteudo);
    }

    [Fact]
    public async Task CriarDesafio_ParaDidRevogado_Recusa()
    {
        var documento = DocumentoAtivo() with { Status = "REVOGADO", Ativo = false };
        var sut = new CriarDesafioAutenticacaoUseCase(
            new DidRegistryFake(documento),
            new DesafioStoreFake(),
            new GeradorNonceFake(),
            new ClockFake(Agora),
            new ConfiguracaoAutenticacaoDid("custodychain-web", TimeSpan.FromMinutes(2)));

        var erro = await Assert.ThrowsAsync<AutenticacaoException>(() => sut.ExecutarAsync(Did));

        Assert.Equal(CodigosErroAutenticacao.CredencialIndisponivel, erro.Codigo);
    }

    [Fact]
    public async Task Concluir_ProvaValida_AutenticaUmaUnicaVez()
    {
        var store = StoreComDesafio(Agora.AddMinutes(2));
        var sut = CriarConclusao(store, new VerificadorFake(true));
        var prova = new ProvaAutenticacao("challenge-1", Did, KeyId, "assinatura");

        var identidade = await sut.ExecutarAsync(prova);
        var replay = await Assert.ThrowsAsync<AutenticacaoException>(() => sut.ExecutarAsync(prova));

        Assert.Equal(Did, identidade.Did);
        Assert.Equal(KeyId, identidade.KeyId);
        Assert.Equal(1, identidade.DocumentVersion);
        Assert.Equal(CodigosErroAutenticacao.DesafioInvalido, replay.Codigo);
    }

    [Fact]
    public async Task Concluir_DesafioExpirado_Recusa()
    {
        var sut = CriarConclusao(StoreComDesafio(Agora.AddSeconds(-1)), new VerificadorFake(true));

        var erro = await Assert.ThrowsAsync<AutenticacaoException>(() => sut.ExecutarAsync(
            new ProvaAutenticacao("challenge-1", Did, KeyId, "assinatura")));

        Assert.Equal(CodigosErroAutenticacao.DesafioExpirado, erro.Codigo);
    }

    [Fact]
    public async Task Concluir_AssinaturaInvalida_ConsomeDesafioERecusa()
    {
        var store = StoreComDesafio(Agora.AddMinutes(2));
        var sut = CriarConclusao(store, new VerificadorFake(false));
        var prova = new ProvaAutenticacao("challenge-1", Did, KeyId, "assinatura-invalida");

        var erro = await Assert.ThrowsAsync<AutenticacaoException>(() => sut.ExecutarAsync(prova));
        var replay = await Assert.ThrowsAsync<AutenticacaoException>(() => sut.ExecutarAsync(prova));

        Assert.Equal(CodigosErroAutenticacao.AssinaturaInvalida, erro.Codigo);
        Assert.Equal(CodigosErroAutenticacao.DesafioInvalido, replay.Codigo);
    }

    [Fact]
    public void VerificadorEd25519_ValidaVetorOficialERecusaMensagemAlterada()
    {
        var metodo = new MetodoVerificacaoDid(
            KeyId,
            "Multikey",
            Did,
            "z6MktwupdmLXVVqTzCw4i46r4uGyosGXRnR3XjN4Zq7oMMsw");
        const string assinatura = "5VZDAMNgrHKQhuLMgG6CioSHfx645dl02HPgZSJJAVVfuIIVkKM7rMYeOXAc-bRr0lv18FlbviRlUUFDjnoQCw";
        var sut = new VerificadorAssinaturaEd25519();

        Assert.True(sut.Verificar(metodo, ReadOnlySpan<byte>.Empty, assinatura));
        Assert.False(sut.Verificar(metodo, "alterado"u8, assinatura));
    }

    private static ConcluirAutenticacaoUseCase CriarConclusao(
        DesafioStoreFake store,
        IVerificadorAssinaturaDid verificador) =>
        new(
            new DidRegistryFake(DocumentoAtivo()),
            store,
            verificador,
            new IdentidadeStoreFake(),
            new ClockFake(Agora));

    private static DesafioStoreFake StoreComDesafio(DateTime expiraEm)
    {
        var store = new DesafioStoreFake();
        store.Armazenar(new DesafioAutenticacaoArmazenado(
            "challenge-1",
            Did,
            KeyId,
            "conteudo"u8.ToArray(),
            expiraEm));
        return store;
    }

    private static DocumentoDidAutenticacao DocumentoAtivo() =>
        new(
            Did,
            2,
            "ATIVO",
            true,
            [new MetodoVerificacaoDid(KeyId, "Multikey", Did, "zChave")],
            [KeyId]);

    private sealed class DidRegistryFake(DocumentoDidAutenticacao? documento) : IDidRegistry
    {
        public Task<DocumentoDidAutenticacao?> ResolverAsync(
            string did,
            CancellationToken cancellationToken = default) => Task.FromResult(documento);
    }

    private sealed class DesafioStoreFake : IDesafioAutenticacaoStore
    {
        public DesafioAutenticacaoArmazenado? Armazenado { get; private set; }

        public void Armazenar(DesafioAutenticacaoArmazenado desafio) => Armazenado = desafio;

        public bool TentarConsumir(string challengeId, out DesafioAutenticacaoArmazenado? desafio)
        {
            desafio = Armazenado?.ChallengeId == challengeId ? Armazenado : null;
            Armazenado = null;
            return desafio is not null;
        }
    }

    private sealed class VerificadorFake(bool valido) : IVerificadorAssinaturaDid
    {
        public bool Verificar(
            MetodoVerificacaoDid metodo,
            ReadOnlySpan<byte> mensagem,
            string assinaturaBase64Url) => valido;
    }

    private sealed class IdentidadeStoreFake : IIdentidadeAutenticacaoStore
    {
        public Task<IdentidadeAutenticada?> ObterAtivaAsync(
            string did,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentidadeAutenticada?>(
                new IdentidadeAutenticada(1, "Admin", Did, "ADMIN", "Administrador"));
    }

    private sealed class GeradorNonceFake : IGeradorNonce
    {
        public byte[] Gerar(int quantidadeBytes) => Enumerable.Repeat((byte)0x2a, quantidadeBytes).ToArray();
    }

    private sealed class ClockFake(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
