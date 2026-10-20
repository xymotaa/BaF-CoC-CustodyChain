namespace CustodyChain.Web.Tests;

public sealed class FrontendShellBaselineTests
{
    [Fact]
    public void LayoutPrincipal_PreservaContratosDoShellEAcessibilidade()
    {
        var layout = Ler("src/CustodyChain.Web/Views/Shared/_Layout.cshtml");

        Assert.Contains("href=\"#conteudo-principal\"", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"sidebar-principal\"", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"sidebar-toggle\"", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"sidebar-scrim\"", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"conteudo-principal\"", layout, StringComparison.Ordinal);
        Assert.Contains("operacao-wallet.js", layout, StringComparison.Ordinal);
        Assert.Contains("shell.js", layout, StringComparison.Ordinal);
        Assert.Contains("@Html.AntiForgeryToken()", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_MantemBreakpointEAcessibilidadeDaSidebar()
    {
        var css = Ler("src/CustodyChain.Web/wwwroot/css/layout.css");
        var script = Ler("src/CustodyChain.Web/wwwroot/js/shell.js");

        Assert.Contains("grid-template-columns: var(--largura-sidebar) minmax(0, 1fr)", css, StringComparison.Ordinal);
        Assert.Contains("min-inline-size: 0", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 56.25rem)", css, StringComparison.Ordinal);
        Assert.Contains("(max-width: 56.25rem)", script, StringComparison.Ordinal);
        Assert.Contains("event.key === \"Escape\"", script, StringComparison.Ordinal);
        Assert.Contains("sidebar.inert", script, StringComparison.Ordinal);
        Assert.Contains("aria-expanded", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("src/CustodyChain.Web/Views/Autenticacao/Entrar.cshtml", "id=\"login-form\"", "@Html.AntiForgeryToken()")]
    [InlineData("src/CustodyChain.Web/Views/Vestigios/Cadastrar.cshtml", "id=\"cadastro-vestigio-form\"", "@Html.AntiForgeryToken()")]
    [InlineData("src/CustodyChain.Web/Views/Movimentacoes/Criar.cshtml", "id=\"criar-remessa-form\"", "@Html.AntiForgeryToken()")]
    [InlineData("src/CustodyChain.Web/Views/GestaoPerfis/Index.cshtml", "data-rotulo=\"DID\"", "@Html.AntiForgeryToken()")]
    public void ViewsCriticas_PreservamHooksEAntiforgery(string caminho, string hook, string antiforgery)
    {
        var view = Ler(caminho);

        Assert.Contains(hook, view, StringComparison.Ordinal);
        Assert.Contains(antiforgery, view, StringComparison.Ordinal);
    }

    [Fact]
    public void Baseline_RegistraCssLocalEMatrizDeValidacao()
    {
        var baseline = Ler("changelog/v0.63.0-baseline-shell-reflow.md");

        Assert.Contains("CSS legado inventariado", baseline, StringComparison.Ordinal);
        Assert.Contains("400%", baseline, StringComparison.Ordinal);
        Assert.Contains("data-operacao-*", baseline, StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentesGlobais_OferecemContratosCanonicosECompatibilidadeExistente()
    {
        var css = Ler("src/CustodyChain.Web/wwwroot/css/componentes.css");
        var tokens = Ler("src/CustodyChain.Web/wwwroot/css/tokens.css");

        Assert.Contains(".pagina-cabecalho-acoes", css, StringComparison.Ordinal);
        Assert.Contains(".botao--primario", css, StringComparison.Ordinal);
        Assert.Contains(".botao--secundario", css, StringComparison.Ordinal);
        Assert.Contains(".botao--terciario", css, StringComparison.Ordinal);
        Assert.Contains(".botao--destrutivo", css, StringComparison.Ordinal);
        Assert.Contains(".botao--compacto", css, StringComparison.Ordinal);
        Assert.Contains(".botao--bloco", css, StringComparison.Ordinal);
        Assert.Contains(".formulario-acoes", css, StringComparison.Ordinal);
        Assert.Contains(".grade-campos.grade-2col", css, StringComparison.Ordinal);
        Assert.Contains("--largura-conteudo", tokens, StringComparison.Ordinal);
        Assert.Contains("--duracao-interacao", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentesGlobais_PreservamReflowDeCamposEAcoes()
    {
        var css = Ler("src/CustodyChain.Web/wwwroot/css/componentes.css");

        Assert.Contains("inline-size: 100%", css, StringComparison.Ordinal);
        Assert.Contains("max-inline-size: 100%", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr))", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", css, StringComparison.Ordinal);
    }

    private static string Ler(string caminhoRelativo)
    {
        for (var diretorio = new DirectoryInfo(AppContext.BaseDirectory); diretorio is not null; diretorio = diretorio.Parent)
        {
            var caminho = Path.Combine(diretorio.FullName, caminhoRelativo);
            if (File.Exists(caminho))
            {
                return File.ReadAllText(caminho);
            }
        }

        throw new FileNotFoundException($"Arquivo não encontrado: {caminhoRelativo}");
    }
}
