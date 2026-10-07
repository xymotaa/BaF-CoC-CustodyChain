using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CustodyChain.Web.Application.SignedOperations;

public sealed class OperacaoAssinadaInvalidaException(string message) : Exception(message);

/// <summary>
/// Envelope imutável transportado pela outbox. A assinatura é validada no
/// gateway e no chaincode; aqui validamos o contrato e a integridade dos bytes
/// persistidos antes de uma tentativa de publicação.
/// </summary>
public sealed class OperacaoAssinadaV1
{
    private const string TipoEsperado = "CustodyChainSignedOperation";
    private const string CanonicalizacaoEsperada = "custodychain-json-c14n-v1";
    private const string AudienciaEsperada = "custodychain-ledger";

    private OperacaoAssinadaV1(JsonElement envelope)
    {
        Envelope = envelope;
        OperationId = envelope.GetProperty("operationId").GetString()!;
    }

    public JsonElement Envelope { get; }
    public string OperationId { get; }

    public static OperacaoAssinadaV1 Ler(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 32 * 1024)
            throw new OperacaoAssinadaInvalidaException("A operação assinada é inválida.");

        try
        {
            using var documento = JsonDocument.Parse(json);
            var envelope = documento.RootElement.Clone();
            Validar(envelope);
            return new OperacaoAssinadaV1(envelope);
        }
        catch (JsonException)
        {
            throw new OperacaoAssinadaInvalidaException("A operação assinada não contém JSON válido.");
        }
    }

    public string CalcularHashCanonicoSemAssinatura()
    {
        var semAssinatura = RemoverAssinatura(Envelope);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalizadorJsonCustodyChain.Canonicalizar(semAssinatura)));
        return Convert.ToHexStringLower(bytes);
    }

    public static void Validar(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object
            || !Texto(envelope, "type", TipoEsperado)
            || !Inteiro(envelope, "version", 1)
            || !Identificador(envelope, "operationId")
            || !Padrao(envelope, "operation", "^[A-Z][A-Z0-9_]{2,63}$")
            || !Objeto(envelope, "payload")
            || !Padrao(envelope, "signerDid", "^did:legal:(admin|custodian|delegate|expert|judge):[a-zA-Z0-9._-]{3,128}$")
            || !ChaveDoSignatario(envelope)
            || !Texto(envelope, "algorithm", "Ed25519")
            || !Texto(envelope, "canonicalization", CanonicalizacaoEsperada)
            || !Texto(envelope, "audience", AudienciaEsperada)
            || !Data(envelope, "timestamp")
            || !Data(envelope, "expiresAt")
            || !Nonce(envelope)
            || !Assinatura(envelope)
            || !JanelaTemporalValida(envelope))
            throw new OperacaoAssinadaInvalidaException("Envelope da operação assinada inválido.");

        ValidarValoresCanonicos(envelope);
    }

    private static JsonElement RemoverAssinatura(JsonElement envelope)
    {
        using var documento = JsonDocument.Parse(envelope.GetRawText());
        using var buffer = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(buffer))
        {
            escritor.WriteStartObject();
            foreach (var propriedade in documento.RootElement.EnumerateObject())
            {
                if (propriedade.NameEquals("signature"))
                    continue;
                propriedade.WriteTo(escritor);
            }
            escritor.WriteEndObject();
        }
        using var resultado = JsonDocument.Parse(buffer.ToArray());
        return resultado.RootElement.Clone();
    }

    private static bool Texto(JsonElement envelope, string nome, string esperado) =>
        envelope.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && valor.GetString() == esperado;

    private static bool Inteiro(JsonElement envelope, string nome, int esperado) =>
        envelope.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.Number
        && valor.TryGetInt32(out var inteiro)
        && inteiro == esperado;

    private static bool Identificador(JsonElement envelope, string nome) =>
        Padrao(envelope, nome, "^urn:uuid:[0-9a-fA-F-]{36}$");

    private static bool Padrao(JsonElement envelope, string nome, string padrao) =>
        envelope.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && System.Text.RegularExpressions.Regex.IsMatch(valor.GetString()!, padrao);

    private static bool Objeto(JsonElement envelope, string nome) =>
        envelope.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.Object;

    private static bool ChaveDoSignatario(JsonElement envelope) =>
        envelope.TryGetProperty("signerDid", out var did)
        && envelope.TryGetProperty("keyId", out var chave)
        && chave.ValueKind == JsonValueKind.String
        && chave.GetString()!.StartsWith($"{did.GetString()}#", StringComparison.Ordinal);

    private static bool Data(JsonElement envelope, string nome) =>
        envelope.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(valor.GetString(), out _);

    private static bool Nonce(JsonElement envelope) =>
        Padrao(envelope, "nonce", "^[A-Za-z0-9_-]{22,128}$");

    private static bool Assinatura(JsonElement envelope) =>
        Padrao(envelope, "signature", "^[A-Za-z0-9_-]{86}$");

    private static bool JanelaTemporalValida(JsonElement envelope)
    {
        var emitidaEm = DateTimeOffset.Parse(envelope.GetProperty("timestamp").GetString()!);
        var expiraEm = DateTimeOffset.Parse(envelope.GetProperty("expiresAt").GetString()!);
        return expiraEm > emitidaEm && expiraEm - emitidaEm <= TimeSpan.FromMinutes(10);
    }

    private static void ValidarValoresCanonicos(JsonElement valor)
    {
        switch (valor.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var propriedade in valor.EnumerateObject())
                    ValidarValoresCanonicos(propriedade.Value);
                return;
            case JsonValueKind.Array:
                foreach (var item in valor.EnumerateArray())
                    ValidarValoresCanonicos(item);
                return;
            case JsonValueKind.Number:
                if (!valor.TryGetInt64(out _))
                    throw new OperacaoAssinadaInvalidaException(
                        "Números de ponto flutuante não são permitidos na operação assinada.");
                return;
            case JsonValueKind.String:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return;
            default:
                throw new OperacaoAssinadaInvalidaException("Valor inválido na operação assinada.");
        }
    }
}

public static class CanonicalizadorJsonCustodyChain
{
    public static string Canonicalizar(JsonElement valor)
    {
        var builder = new StringBuilder();
        Escrever(valor, builder);
        return builder.ToString();
    }

    private static void Escrever(JsonElement valor, StringBuilder builder)
    {
        switch (valor.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var primeira = true;
                foreach (var propriedade in valor.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (!primeira) builder.Append(',');
                    primeira = false;
                    builder.Append(JsonSerializer.Serialize(propriedade.Name));
                    builder.Append(':');
                    Escrever(propriedade.Value, builder);
                }
                builder.Append('}');
                return;
            case JsonValueKind.Array:
                builder.Append('[');
                for (var indice = 0; indice < valor.GetArrayLength(); indice++)
                {
                    if (indice > 0) builder.Append(',');
                    Escrever(valor[indice], builder);
                }
                builder.Append(']');
                return;
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(valor.GetString()));
                return;
            case JsonValueKind.Number:
                if (!valor.TryGetInt64(out var inteiro))
                    throw new OperacaoAssinadaInvalidaException("Número não inteiro na operação assinada.");
                builder.Append(inteiro.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            case JsonValueKind.True:
                builder.Append("true");
                return;
            case JsonValueKind.False:
                builder.Append("false");
                return;
            case JsonValueKind.Null:
                builder.Append("null");
                return;
            default:
                throw new OperacaoAssinadaInvalidaException("Valor inválido para canonicalização.");
        }
    }
}
