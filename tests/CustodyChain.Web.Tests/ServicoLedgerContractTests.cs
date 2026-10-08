using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class ServicoLedgerContractTests
{
    [Fact]
    public async Task ServicoLedgerFabric_EnviaProvaDeRegistroAoContratoV2()
    {
        var handler = new HandlerCapturandoRequisicao();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://ledger.local") };
        var ledger = new ServicoLedgerFabric(httpClient);
        var command = new
        {
            type = "CustodyChainDidRegistration",
            version = 1,
            commandId = "urn:uuid:11111111-1111-1111-1111-111111111111",
            audience = "custodychain-ledger",
            issuedAt = "2027-01-01T00:00:00.0000000Z",
            expiresAt = "2027-01-01T00:05:00.0000000Z"
        };

        await ledger.RegistrarDidV2PendenteAsync(new RegistroDidPendenteDto(command, "assinatura"));

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v2/dids/pending", handler.Path);
        using var corpo = JsonDocument.Parse(handler.Body!);
        Assert.Equal("CustodyChainDidRegistration", corpo.RootElement.GetProperty("command").GetProperty("type").GetString());
        Assert.Equal("assinatura", corpo.RootElement.GetProperty("signature").GetString());
    }

    [Fact]
    public async Task ServicoLedgerFabric_EnviaVcAssinadaParaARotaV2()
    {
        var handler = new HandlerCapturandoRequisicao();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://ledger.local") };
        var ledger = new ServicoLedgerFabric(httpClient);
        using var document = JsonDocument.Parse("""
            {"id":"urn:uuid:11111111-1111-1111-1111-111111111111","issuer":"did:legal:admin:teste"}
            """);

        var identificador = await ledger.EmitirCredencialPermissaoV2Async(
            new CredencialPermissaoV2Dto(document.RootElement.Clone()));

        Assert.Equal("cred-coc-estavel", identificador);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v2/credenciais/permissao", handler.Path);
        using var corpo = JsonDocument.Parse(handler.Body!);
        Assert.Equal("urn:uuid:11111111-1111-1111-1111-111111111111", corpo.RootElement.GetProperty("credential").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ServicoLedgerFabric_EnviaOperacaoAssinadaParaARotaV2()
    {
        var handler = new HandlerCapturandoRequisicao();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://ledger.local") };
        var ledger = new ServicoLedgerFabric(httpClient);
        using var document = JsonDocument.Parse("""
            {"operationId":"urn:uuid:11111111-1111-1111-1111-111111111111"}
            """);

        var operationId = await ledger.RegistrarOperacaoAssinadaV1Async(
            new OperacaoAssinadaV1Dto(document.RootElement.Clone()));

        Assert.Equal("urn:uuid:11111111-1111-1111-1111-111111111111", operationId);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/v2/operacoes", handler.Path);
        using var corpo = JsonDocument.Parse(handler.Body!);
        Assert.Equal(operationId, corpo.RootElement.GetProperty("operation").GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task ServicoLedgerFabric_EnviaDuasProvasParaRotacaoDeChave()
    {
        var handler = new HandlerCapturandoRequisicao();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://ledger.local") };
        var ledger = new ServicoLedgerFabric(httpClient);
        using var document = JsonDocument.Parse("""
            {"type":"CustodyChainDidKeyRotation","subjectDid":"did:legal:expert:teste"}
            """);

        var resultado = await ledger.RotacionarChaveDidV2Async(
            "did:legal:expert:teste",
            new RotacaoChaveDidV2Dto(
                document.RootElement.Clone(),
                new ProvaChaveDidLedgerDto("did:legal:expert:teste#key-1", "Ed25519", "atual"),
                new ProvaChaveDidLedgerDto("did:legal:expert:teste#key-2", "Ed25519", "nova")));

        Assert.Equal("/v2/dids/did%3Alegal%3Aexpert%3Ateste/rotate-key", handler.Path);
        Assert.Equal(3, resultado.DocumentVersion);
        Assert.Equal("did:legal:expert:teste#key-2", resultado.KeyId);
        using var corpo = JsonDocument.Parse(handler.Body!);
        Assert.Equal("atual", corpo.RootElement.GetProperty("currentKeyProof").GetProperty("signature").GetString());
        Assert.Equal("nova", corpo.RootElement.GetProperty("newKeyProof").GetProperty("signature").GetString());
    }

    [Fact]
    public async Task ServicoLedgerFabric_ConsultaOperacaoAssinadaRegistradaNoLedger()
    {
        var handler = new HandlerCapturandoRequisicao();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://ledger.local") };
        var ledger = new ServicoLedgerFabric(httpClient);

        var resultado = await ledger.ObterOperacaoAssinadaV1Async("urn:uuid:11111111-1111-1111-1111-111111111111");

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/v2/operacoes/urn%3Auuid%3A11111111-1111-1111-1111-111111111111", handler.Path);
        Assert.Equal("COLETA_REGISTRAR", resultado.SignedOperation.GetProperty("operation").GetString());
    }

    private sealed class HandlerCapturandoRequisicao : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri!.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var content = Path == "/v2/dids/did%3Alegal%3Aexpert%3Ateste/rotate-key"
                ? JsonContent.Create(new
                {
                    id = "did:legal:expert:teste",
                    did = "did:legal:expert:teste",
                    documentVersion = 3,
                    authentication = new[] { "did:legal:expert:teste#key-2" }
                })
                : Path == "/v2/operacoes/urn%3Auuid%3A11111111-1111-1111-1111-111111111111"
                ? JsonContent.Create(new
                {
                    signedOperation = new { operation = "COLETA_REGISTRAR" }
                })
                : Path == "/v2/operacoes"
                    ? JsonContent.Create(new { operationId = "urn:uuid:11111111-1111-1111-1111-111111111111" })
                    : JsonContent.Create(new { credencialId = "cred-coc-estavel" });

            return new HttpResponseMessage(Method == HttpMethod.Get ? HttpStatusCode.OK : HttpStatusCode.Created)
            {
                Content = content
            };
        }
    }
}
