using CustodyChain.Web.Application.RevogacaoDid;

namespace CustodyChain.Web.Tests;

public sealed class PoliticaRevogacaoDidTests
{
    [Fact]
    public void RecusaAutoRevogacaoMesmoComComandoValido() =>
        Assert.Equal(MotivoRejeicaoRevogacaoDid.AutoRevogacao,
            PoliticaRevogacaoDid.Avaliar(true, "did:legal:admin:a", "did:legal:admin:a", false, true));

    [Fact]
    public void RecusaResponsabilidadePendente() =>
        Assert.Equal(MotivoRejeicaoRevogacaoDid.ResponsabilidadesPendentes,
            PoliticaRevogacaoDid.Avaliar(true, "did:legal:admin:a", "did:legal:expert:b", true, true));

    [Fact]
    public void AceitaRevogacaoElegivel() =>
        Assert.Equal(MotivoRejeicaoRevogacaoDid.Nenhum,
            PoliticaRevogacaoDid.Avaliar(true, "did:legal:admin:a", "did:legal:expert:b", false, true));
}
