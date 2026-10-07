using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.SignedOperations;

namespace CustodyChain.Web.Tests;

public sealed class OperacaoAssinadaV1Tests
{
    [Fact]
    public void Canonicalizar_VetorCompartilhado_ProduzTextoEHashEsperados()
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "contracts", "signed-operation-v1-vectors.json")));
        var vetor = documento.RootElement.GetProperty("vectors")[0];

        var canonical = CanonicalizadorJsonCustodyChain.Canonicalizar(vetor.GetProperty("unsigned"));
        var assinada = AssinarParaContrato(vetor.GetProperty("unsigned"));
        var operacao = OperacaoAssinadaV1.Ler(assinada.GetRawText());

        Assert.Equal(vetor.GetProperty("canonical").GetString(), canonical);
        Assert.Equal(vetor.GetProperty("sha256").GetString(), operacao.CalcularHashCanonicoSemAssinatura());
    }

    [Fact]
    public void Ler_RecusaNumeroDePontoFlutuante()
    {
        var operacao = AssinarParaContrato(JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = "urn:uuid:11111111-1111-1111-1111-111111111111",
            operation = "LAUDO_EMITIR",
            payload = new { quantidade = 1.5 },
            signerDid = "did:legal:expert:teste-001",
            keyId = "did:legal:expert:teste-001#key-1",
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            timestamp = "2026-10-06T12:00:00.000Z",
            expiresAt = "2026-10-06T12:05:00.000Z",
            nonce = "0123456789abcdefghijkl"
        }));

        Assert.Throws<OperacaoAssinadaInvalidaException>(() => OperacaoAssinadaV1.Ler(operacao.GetRawText()));
    }

    private static JsonElement AssinarParaContrato(JsonElement unsigned)
    {
        var node = JsonNode.Parse(unsigned.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }
}
