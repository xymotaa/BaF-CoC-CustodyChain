using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.VerifiableCredentials;

namespace CustodyChain.Web.Tests;

public class CriarVcPermissaoTests
{
    private static readonly DateTime Agora = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Criar_ConstroiEnvelopeCanonicoSemProva()
    {
        var sut = new CriarVcPermissao(new ClockFixo());

        var resultado = sut.Executar(new CriarVcPermissaoInput(
            "did:legal:admin:emissor", "did:legal:expert:titular", "PERITO", 42,
            Agora.AddHours(1)));

        var credential = resultado.Credencial;
        Assert.StartsWith("urn:uuid:", resultado.Id);
        Assert.Equal("did:legal:admin:emissor", credential.GetProperty("issuer").GetString());
        Assert.Equal("did:legal:expert:titular", credential.GetProperty("credentialSubject").GetProperty("id").GetString());
        Assert.Equal("PERITO", credential.GetProperty("credentialSubject").GetProperty("perfil").GetString());
        Assert.Equal("42", credential.GetProperty("credentialSubject").GetProperty("processoId").GetString());
        Assert.Equal($"{resultado.Id}#status", credential.GetProperty("credentialStatus").GetProperty("id").GetString());
        Assert.False(credential.TryGetProperty("proof", out _));
    }

    [Fact]
    public void Criar_RecusaExpiracaoAnteriorAEmissao()
    {
        var sut = new CriarVcPermissao(new ClockFixo());

        Assert.Throws<ArgumentException>(() => sut.Executar(new CriarVcPermissaoInput(
            "did:legal:admin:emissor", "did:legal:expert:titular", "PERITO", null,
            Agora)));
    }

    private sealed class ClockFixo : IClock
    {
        public DateTime UtcNow => Agora;
    }
}
