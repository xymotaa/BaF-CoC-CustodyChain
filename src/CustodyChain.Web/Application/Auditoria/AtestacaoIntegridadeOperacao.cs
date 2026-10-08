using System.Text.Json;
using System.Text.RegularExpressions;

namespace CustodyChain.Web.Application.Auditoria;

public sealed class AtestacaoIntegridadeOperacaoInvalidaException(string message) : Exception(message);

public sealed record AtestacaoIntegridadeOperacao(
    string ContentHashSha256,
    string ContentCid,
    long ByteLength,
    string MediaType,
    string FileName);

public static class LeitorAtestacaoIntegridadeOperacao
{
    private static readonly Regex HashSha256 = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);

    public static AtestacaoIntegridadeOperacao? LerColeta(
        JsonElement operacao,
        string assetRefEsperado,
        string rotuloEvidenciaEsperado)
    {
        if (operacao.ValueKind != JsonValueKind.Object
            || !Texto(operacao, "operation", "COLETA_REGISTRAR")
            || !operacao.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object
            || !Texto(payload, "assetRef", assetRefEsperado)
            || !Texto(payload, "rotuloEvidencia", rotuloEvidenciaEsperado)
            || !payload.TryGetProperty("integrity", out var integridade))
        {
            throw new AtestacaoIntegridadeOperacaoInvalidaException(
                "A operação consultada não corresponde ao vestígio solicitado.");
        }

        if (integridade.ValueKind == JsonValueKind.Null)
            return null;

        if (integridade.ValueKind != JsonValueKind.Object
            || !Texto(integridade, "algorithm", "SHA-256")
            || !Hash(integridade, "contentHashSha256")
            || !TextoObrigatorio(integridade, "contentCid")
            || !InteiroPositivo(integridade, "byteLength")
            || !TextoObrigatorio(integridade, "mediaType")
            || !TextoObrigatorio(integridade, "fileName"))
        {
            throw new AtestacaoIntegridadeOperacaoInvalidaException(
                "A atestação de integridade da operação é inválida.");
        }

        return new AtestacaoIntegridadeOperacao(
            integridade.GetProperty("contentHashSha256").GetString()!,
            integridade.GetProperty("contentCid").GetString()!,
            integridade.GetProperty("byteLength").GetInt64(),
            integridade.GetProperty("mediaType").GetString()!,
            integridade.GetProperty("fileName").GetString()!);
    }

    private static bool Texto(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

    private static bool Hash(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && HashSha256.IsMatch(valor.GetString()!);

    private static bool TextoObrigatorio(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(valor.GetString());

    private static bool InteiroPositivo(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.Number
        && valor.TryGetInt64(out var numero)
        && numero > 0;
}
