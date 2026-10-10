using CustodyChain.Web.Services.Ledger;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CustodyChain.Web.Tests;

public sealed class OrganizacaoFabricDidTests
{
    [Theory]
    [InlineData("did:legal:admin:governanca", OrganizacaoFabricDid.Org1Msp)]
    [InlineData("did:legal:delegate:coletor", OrganizacaoFabricDid.Org1Msp)]
    [InlineData("did:legal:judge:externo", OrganizacaoFabricDid.Org1Msp)]
    [InlineData("did:legal:custodian:guarda", OrganizacaoFabricDid.Org2Msp)]
    [InlineData("did:legal:expert:perito", OrganizacaoFabricDid.Org2Msp)]
    public void ResolveMspPelaPoliticaDoMetodoDid(string did, string mspEsperado)
    {
        Assert.Equal(mspEsperado, OrganizacaoFabricDid.ResolverMsp(did));
    }

    [Fact]
    public void RecusaDidSemPoliticaFabric()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OrganizacaoFabricDid.ResolverMsp("did:legal:unknown:sem-politica"));

        Assert.Contains("sem organização Fabric", exception.Message);
    }

    [Fact]
    public async Task RoteiaOperacaoDoPeritoSomenteParaOGatewayDaOrg2()
    {
        var org1 = new HandlerCapturandoRequisicao();
        var org2 = new HandlerCapturandoRequisicao();
        var factory = new HttpClientFactoryFake(new Dictionary<string, HttpClient>
        {
            [ServicoLedgerPorOrganizacao.NomeCliente(OrganizacaoFabricDid.Org1Msp)] = CriarCliente(org1),
            [ServicoLedgerPorOrganizacao.NomeCliente(OrganizacaoFabricDid.Org2Msp)] = CriarCliente(org2)
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ledger:Organizations:Org1MSP:GatewayUrl"] = "http://org1.local",
            ["Ledger:Organizations:Org2MSP:GatewayUrl"] = "http://org2.local"
        }).Build();
        var router = new ServicoLedgerPorOrganizacao(factory, configuration);
        using var document = JsonDocument.Parse("""{"signerDid":"did:legal:expert:perito"}""");

        await router.RegistrarOperacaoAssinadaV1Async(new OperacaoAssinadaV1Dto(document.RootElement.Clone()));

        Assert.Null(org1.Path);
        Assert.Equal("/v2/operacoes", org2.Path);
    }

    private static HttpClient CriarCliente(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://ledger.local") };

    private sealed class HttpClientFactoryFake(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class HandlerCapturandoRequisicao : HttpMessageHandler
    {
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { operationId = "urn:uuid:11111111-1111-1111-1111-111111111111" })
            });
        }
    }
}
