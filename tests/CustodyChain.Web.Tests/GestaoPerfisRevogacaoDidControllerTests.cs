using System.Security.Claims;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.RevogacaoDid;
using CustodyChain.Web.Application.VerifiableCredentials;
using CustodyChain.Web.Controllers;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using CustodyChain.Web.Services.VerifiableCredentials;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;

namespace CustodyChain.Web.Tests;

public sealed class GestaoPerfisRevogacaoDidControllerTests
{
    [Theory]
    [InlineData(EstadoRevogacaoDid.ConfirmadaNoLedger, StatusCodes.Status200OK)]
    [InlineData(EstadoRevogacaoDid.PendenteLedger, StatusCodes.Status202Accepted)]
    [InlineData(EstadoRevogacaoDid.ProcessandoLedger, StatusCodes.Status202Accepted)]
    [InlineData(EstadoRevogacaoDid.FalhaReconciliacao, StatusCodes.Status202Accepted)]
    [InlineData(EstadoRevogacaoDid.FalhaPermanente, StatusCodes.Status400BadRequest)]
    public async Task RevogarComProva_RetornaStatusCompativelComEstadoDaSaga(EstadoRevogacaoDid estado, int statusEsperado)
    {
        var controller = CriarController(new RevogarDidFake(estado));

        var resultado = await controller.RevogarComProva(11,
            new EnviarProvaRevogacaoDidRequest(new { commandId = "urn:uuid:11111111-1111-1111-1111-111111111111" }, "key-1", "signature"),
            CancellationToken.None);

        var resposta = Assert.IsAssignableFrom<ObjectResult>(resultado);
        Assert.Equal(statusEsperado, resposta.StatusCode);
    }

    [Fact]
    public async Task CriarComandoRevogacaoDid_Retorna503QuandoResolverEstaIndisponivel()
    {
        var controller = CriarController(new RevogarDidFake(EstadoRevogacaoDid.ConfirmadaNoLedger)
        {
            FalhaPreparacao = new LedgerRevogacaoDidIndisponivelException("ledger indisponível")
        });

        var resultado = await controller.CriarComandoRevogacaoDid(11, CancellationToken.None);

        var resposta = Assert.IsAssignableFrom<ObjectResult>(resultado);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, resposta.StatusCode);
    }

    private static GestaoPerfisController CriarController(RevogarDidFake revogarDid)
    {
        var db = new CustodyChainDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CustodyChainDbContext>().Options);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim("did", "did:legal:admin:teste")
            ], "test"))
        };
        var controller = new GestaoPerfisController(db, new LedgerFake(), new DidRegistryFake(), revogarDid,
            new ConfigurationBuilder().Build(), new CriarVcPermissao(new ClockFake()), new MemoryEmissaoVcPendenteStore())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };
        controller.Url = new UrlHelperFake();
        return controller;
    }

    private sealed class RevogarDidFake(EstadoRevogacaoDid estado) : IRevogarDid
    {
        public Exception? FalhaPreparacao { get; init; }

        public Task<PreparacaoRevogacaoDid> PrepararAsync(PrepararRevogacaoDidCommand command, CancellationToken cancellationToken = default)
        {
            if (FalhaPreparacao is not null) throw FalhaPreparacao;
            throw new InvalidOperationException("Este teste só valida o mapeamento de erro.");
        }

        public Task<ResultadoRevogacaoDid> ExecutarAsync(ExecutarRevogacaoDidCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResultadoRevogacaoDid("Perito B", estado));
    }

    private sealed class DidRegistryFake : IDidRegistry
    {
        public Task<DocumentoDidAutenticacao?> ResolverAsync(string did, CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentoDidAutenticacao?>(null);
    }

    private sealed class ClockFake : IClock
    {
        public DateTime UtcNow => new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class UrlHelperFake : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/gestao-perfis";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/gestao-perfis";
        public string? RouteUrl(UrlRouteContext routeContext) => "/gestao-perfis";
    }
}
