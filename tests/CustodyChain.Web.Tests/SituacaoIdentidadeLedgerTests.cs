using CustodyChain.Web.Models.Entities;

namespace CustodyChain.Web.Tests;

public sealed class SituacaoIdentidadeLedgerTests
{
    [Fact]
    public void NovoIntervenienteNaoDeclaraIdentidadeLedgerAtivaPorPadrao()
    {
        var interveniente = new Interveniente
        {
            Did = "did:legal:expert:novo",
            Nome = "Novo interveniente"
        };

        Assert.Equal(SituacaoIdentidadeLedger.DESCONHECIDA, interveniente.SituacaoIdentidadeLedger);
    }
}
