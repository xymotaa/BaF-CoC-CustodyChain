using System.Text.RegularExpressions;

namespace CustodyChain.Web.Tests;

public sealed class WalletOperationViewsTests
{
    [Theory]
    [InlineData("src/CustodyChain.Web/Views/Autenticacao/RecuperarIdentidade.cshtml", 4)]
    [InlineData("src/CustodyChain.Web/Views/GestaoPerfis/RecuperarChave.cshtml", 2)]
    [InlineData("src/CustodyChain.Web/Views/GestaoPerfis/RevogarDidV2.cshtml", 2)]
    public void DefinirProcessamento_UsaPainelDeclarado(string caminhoRelativo, int quantidadeEsperada)
    {
        var conteudo = File.ReadAllText(LocalizarArquivoNoRepositorio(caminhoRelativo));
        var chamadas = Regex.Matches(
            conteudo,
            @"wallet\.definirProcessamento\(\{(?<argumentos>[^}]*)\}\)",
            RegexOptions.CultureInvariant);

        Assert.Equal(quantidadeEsperada, chamadas.Count);
        Assert.All(chamadas.Cast<Match>(), chamada =>
            Assert.Contains("painel: panel", chamada.Groups["argumentos"].Value, StringComparison.Ordinal));
        Assert.DoesNotContain("painel,", conteudo, StringComparison.Ordinal);
    }

    private static string LocalizarArquivoNoRepositorio(string caminhoRelativo)
    {
        for (var diretorio = new DirectoryInfo(AppContext.BaseDirectory); diretorio is not null; diretorio = diretorio.Parent)
        {
            var caminho = Path.Combine(diretorio.FullName, caminhoRelativo);
            if (File.Exists(caminho))
            {
                return caminho;
            }
        }

        throw new FileNotFoundException($"Arquivo de view não encontrado: {caminhoRelativo}");
    }
}
