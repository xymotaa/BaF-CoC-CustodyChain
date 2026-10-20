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

    [Theory]
    [InlineData("src/CustodyChain.Web/Views/Vestigios/Cadastrar.cshtml")]
    [InlineData("src/CustodyChain.Web/Views/Movimentacoes/Criar.cshtml")]
    [InlineData("src/CustodyChain.Web/Views/Recebimento/Index.cshtml")]
    [InlineData("src/CustodyChain.Web/Views/Arquivo/Entrada.cshtml")]
    [InlineData("src/CustodyChain.Web/Views/DestinacaoFinal/Index.cshtml")]
    [InlineData("src/CustodyChain.Web/Views/DestinacaoFinal/Solicitar.cshtml")]
    public void OperacoesDeCustodia_UsamHelperDaWalletELimpamCredenciais(string caminhoRelativo)
    {
        var conteudo = File.ReadAllText(LocalizarArquivoNoRepositorio(caminhoRelativo));

        Assert.Contains("window.CustodyChainWallet", conteudo, StringComparison.Ordinal);
        Assert.Contains("walletUi.requisicaoWallet", conteudo, StringComparison.Ordinal);
        Assert.Contains("walletUi.limparCamposSensíveis(password, pairing)", conteudo, StringComparison.Ordinal);
        Assert.Contains("walletUi.definirProcessamento", conteudo, StringComparison.Ordinal);
    }

    [Fact]
    public void ElementosHidden_PermanecemOcultosMesmoComClassesDeLayout()
    {
        var cssBase = File.ReadAllText(LocalizarArquivoNoRepositorio("src/CustodyChain.Web/wwwroot/css/base.css"));
        var login = File.ReadAllText(LocalizarArquivoNoRepositorio("src/CustodyChain.Web/Views/Autenticacao/Entrar.cshtml"));

        Assert.Matches(@"\[hidden\]\s*\{\s*display:\s*none\s*!important;", cssBase);
        Assert.Contains("id=\"login-erro\" class=\"alerta alerta-erro\"", login, StringComparison.Ordinal);
        Assert.Contains("hidden", login, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessamentoPericial_ReutilizaHelperDaWalletEScriptDedicado()
    {
        var index = File.ReadAllText(LocalizarArquivoNoRepositorio("src/CustodyChain.Web/Views/ProcessamentoPericial/Index.cshtml"));
        var parcial = File.ReadAllText(LocalizarArquivoNoRepositorio("src/CustodyChain.Web/Views/ProcessamentoPericial/_PericiaCard.cshtml"));
        var script = File.ReadAllText(LocalizarArquivoNoRepositorio("src/CustodyChain.Web/wwwroot/js/ui/processamento-pericial.js"));

        Assert.Contains("_PericiaCard", index, StringComparison.Ordinal);
        Assert.Contains("processamento-pericial.js", index, StringComparison.Ordinal);
        Assert.Contains("data-operacao-assinada-form", parcial, StringComparison.Ordinal);
        Assert.Contains("data-laudo-form", parcial, StringComparison.Ordinal);
        Assert.Contains("window.CustodyChainWallet", script, StringComparison.Ordinal);
        Assert.Contains("walletUi.limparCamposSensíveis(ui.senha, ui.pareamento)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AprovacaoPericia_ReutilizaWalletELimpaCredenciais()
    {
        var conteudo = File.ReadAllText(LocalizarArquivoNoRepositorio("src/CustodyChain.Web/Views/Pericias/Aprovar.cshtml"));

        Assert.Contains("window.CustodyChainWallet", conteudo, StringComparison.Ordinal);
        Assert.Contains("walletUi.requisicaoAplicacao", conteudo, StringComparison.Ordinal);
        Assert.Contains("walletUi.requisicaoWallet", conteudo, StringComparison.Ordinal);
        Assert.Contains("walletUi.limparCamposSensíveis(password, pairing)", conteudo, StringComparison.Ordinal);
        Assert.Contains("password.required = false", conteudo, StringComparison.Ordinal);
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
