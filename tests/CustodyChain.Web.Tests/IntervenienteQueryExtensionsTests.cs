using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Tests;

public sealed class IntervenienteQueryExtensionsTests
{
    [Fact]
    public void ModeloNaoDefineFiltroGlobalParaInterveniente()
    {
        using var db = CriarContexto();

        var entidade = db.Model.FindEntityType(typeof(Interveniente));

        Assert.NotNull(entidade);
        Assert.Null(entidade.GetQueryFilter());
    }

    [Fact]
    public void ConsultaOperacionalExigeCadastroEIdentidadeAtivos()
    {
        using var db = CriarContexto();

        var sql = db.Intervenientes.AptosParaOperacoesLedger().ToQueryString();

        Assert.Contains("Situacao", sql);
        Assert.Contains("SituacaoIdentidadeLedger", sql);
        Assert.Contains("ATIVA", sql);
    }

    private static CustodyChainDbContext CriarContexto()
    {
        var opcoes = new DbContextOptionsBuilder<CustodyChainDbContext>()
            .UseMySql(
                "Server=localhost;Database=custodychain_testes;User=root;Password=nao-usado",
                new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;

        return new CustodyChainDbContext(opcoes);
    }
}
