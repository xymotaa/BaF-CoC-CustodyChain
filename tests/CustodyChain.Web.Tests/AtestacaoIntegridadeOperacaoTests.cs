using System.Text.Json;
using CustodyChain.Web.Application.Auditoria;

namespace CustodyChain.Web.Tests;

public sealed class AtestacaoIntegridadeOperacaoTests
{
    [Fact]
    public void LerColeta_ComAtestacaoValida_RetornaHashAncorado()
    {
        using var documento = JsonDocument.Parse("""
            {
              "operation":"COLETA_REGISTRAR",
              "payload":{
                "assetRef":"urn:uuid:11111111-1111-1111-1111-111111111111",
                "rotuloEvidencia":"RE-001",
                "integrity":{
                  "algorithm":"SHA-256",
                  "contentHashSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "contentCid":"bafyteste",
                  "byteLength":8,
                  "mediaType":"text/plain",
                  "fileName":"evidencia.txt"
                }
              }
            }
            """);

        var atestacao = LeitorAtestacaoIntegridadeOperacao.LerColeta(
            documento.RootElement,
            "urn:uuid:11111111-1111-1111-1111-111111111111",
            "RE-001");

        Assert.NotNull(atestacao);
        Assert.Equal(new string('a', 64), atestacao.ContentHashSha256);
        Assert.Equal("bafyteste", atestacao.ContentCid);
    }

    [Fact]
    public void LerColeta_ComAtestacaoNula_IndicaQueArquivoNaoFoiVinculado()
    {
        using var documento = JsonDocument.Parse("""
            {"operation":"COLETA_REGISTRAR","payload":{"assetRef":"urn:uuid:11111111-1111-1111-1111-111111111111","rotuloEvidencia":"RE-001","integrity":null}}
            """);

        var atestacao = LeitorAtestacaoIntegridadeOperacao.LerColeta(
            documento.RootElement,
            "urn:uuid:11111111-1111-1111-1111-111111111111",
            "RE-001");

        Assert.Null(atestacao);
    }

    [Fact]
    public void LerColeta_ComAtivoDiferente_RecusaOperacaoMesmoQueTenhaHashValido()
    {
        using var documento = JsonDocument.Parse("""
            {"operation":"COLETA_REGISTRAR","payload":{"assetRef":"urn:uuid:outro","rotuloEvidencia":"RE-001","integrity":null}}
            """);

        Assert.Throws<AtestacaoIntegridadeOperacaoInvalidaException>(() =>
            LeitorAtestacaoIntegridadeOperacao.LerColeta(
                documento.RootElement,
                "urn:uuid:11111111-1111-1111-1111-111111111111",
                "RE-001"));
    }
}
