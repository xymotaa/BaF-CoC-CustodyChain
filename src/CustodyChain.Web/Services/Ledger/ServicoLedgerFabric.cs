using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustodyChain.Web.Services.Ledger;

/// <summary>
/// Implementação real de <see cref="IServicoLedger"/>: chama o gateway
/// HTTP (fabric/gateway/) que fala com o chaincode CustodyChain via
/// Fabric Gateway (fabric/chaincode/). Troca o <see cref="LedgerFake"/>
/// sem alterar nenhum controller.
/// </summary>
public class ServicoLedgerFabric(HttpClient httpClient) : IServicoLedger
{
    private static readonly JsonSerializerOptions OpcoesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task RegistrarDidV2PendenteAsync(
        RegistroDidPendenteDto dto,
        CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsJsonAsync("/v2/dids/pending", new
        {
            command = dto.Command,
            signature = dto.Signature
        }, OpcoesJson, cancellationToken);
        await LancarSeFalhaAsync(resposta);
    }

    public async Task AtivarDidV2Async(
        string did,
        AtivacaoDidV2Dto dto,
        CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsJsonAsync(
            $"/v2/dids/{Uri.EscapeDataString(did)}/activate", new
            {
                command = dto.Command,
                keyId = dto.KeyId,
                signature = dto.Signature
            }, OpcoesJson, cancellationToken);
        await LancarSeFalhaAsync(resposta);
    }

    public async Task<DocumentoDidRotacionadoDto> RotacionarChaveDidV2Async(
        string did,
        RotacaoChaveDidV2Dto dto,
        CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsJsonAsync(
            $"/v2/dids/{Uri.EscapeDataString(did)}/rotate-key", new
            {
                command = dto.Command,
                currentKeyProof = dto.CurrentKeyProof,
                newKeyProof = dto.NewKeyProof
            }, OpcoesJson, cancellationToken);
        await LancarSeFalhaAsync(resposta);

        var documento = await resposta.Content.ReadFromJsonAsync<DocumentoRotacionadoGatewayDto>(
            OpcoesJson, cancellationToken)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao rotacionar a chave DID.");
        var keyId = documento.Authentication.FirstOrDefault()
            ?? throw new InvalidOperationException("Documento DID rotacionado sem chave de autenticação ativa.");
        return new DocumentoDidRotacionadoDto(documento.Id ?? documento.Did, documento.DocumentVersion, keyId);
    }

    public async Task<DocumentoDidRotacionadoDto> RecuperarChaveDidV2Async(string did, RecuperacaoChaveDidV2Dto dto, CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsJsonAsync($"/v2/dids/{Uri.EscapeDataString(did)}/recover-key", new { command = dto.Command, dto.AdminKeyId, dto.AdminSignature, dto.CandidateSignature }, OpcoesJson, cancellationToken);
        await LancarSeFalhaAsync(resposta);
        var documento = await resposta.Content.ReadFromJsonAsync<DocumentoRotacionadoGatewayDto>(OpcoesJson, cancellationToken)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao recuperar a chave DID.");
        var keyId = documento.Authentication.FirstOrDefault() ?? throw new InvalidOperationException("Documento DID recuperado sem chave de autenticação ativa.");
        return new DocumentoDidRotacionadoDto(documento.Id ?? documento.Did, documento.DocumentVersion, keyId);
    }

    public async Task<string> GerarDidAsync(TipoAtor tipo)
    {
        var metodoDid = $"did:legal:{tipo.ToString().ToLowerInvariant()}";
        var did = $"{metodoDid}:{Guid.NewGuid():N}";
        var corpo = new { did, metodoDid };

        var resposta = await httpClient.PostAsJsonAsync("/dids", corpo, OpcoesJson);
        await LancarSeFalhaAsync(resposta);

        return did;
    }

    public async Task AtivarDidAsync(string did, string didEmissor, string senhaEmissor)
    {
        var corpo = new { didEmissor };
        var resposta = await httpClient.PostAsJsonAsync($"/dids/{Uri.EscapeDataString(did)}/ativar", corpo, OpcoesJson);
        await LancarSeFalhaAsync(resposta);
    }

    public async Task<DidDocument> ResolverDidAsync(string did)
    {
        var resposta = await httpClient.GetAsync($"/dids/{Uri.EscapeDataString(did)}");
        await LancarSeFalhaAsync(resposta);

        var dto = await resposta.Content.ReadFromJsonAsync<DidDocumentoGatewayDto>(OpcoesJson)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao resolver DID.");

        return new DidDocument(dto.Did, dto.MetodoDid, dto.Ativo);
    }

    public async Task<string> EmitirCredencialPermissaoV2Async(
        CredencialPermissaoV2Dto dto,
        CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsJsonAsync("/v2/credenciais/permissao", new
        {
            credential = dto.Credential
        }, OpcoesJson, cancellationToken);
        await LancarSeFalhaAsync(resposta);

        var resultado = await resposta.Content.ReadFromJsonAsync<CredencialEmitidaGatewayDto>(OpcoesJson, cancellationToken)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao emitir VC de permissão.");
        return resultado.CredencialId;
    }

    public async Task RevogarCredencialV2Async(
        string credencialId,
        RevogacaoCredencialV2Dto dto,
        CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsJsonAsync(
            $"/v2/credenciais/{Uri.EscapeDataString(credencialId)}/revogar", new
            {
                command = dto.Command,
                keyId = dto.KeyId,
                signature = dto.Signature
            }, OpcoesJson, cancellationToken);
        await LancarSeFalhaAsync(resposta);
    }

    public async Task<string> RegistrarOperacaoAssinadaV1Async(
        OperacaoAssinadaV1Dto dto,
        CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsJsonAsync("/v2/operacoes", new
        {
            operation = dto.Operation
        }, OpcoesJson, cancellationToken);
        await LancarSeFalhaAsync(resposta);

        var resultado = await resposta.Content.ReadFromJsonAsync<OperacaoAssinadaGatewayDto>(OpcoesJson, cancellationToken)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao registrar a operação assinada.");
        return resultado.OperationId;
    }

    public async Task<OperacaoAssinadaRegistradaV1Dto> ObterOperacaoAssinadaV1Async(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.GetAsync(
            $"/v2/operacoes/{Uri.EscapeDataString(operationId)}", cancellationToken);
        await LancarSeFalhaAsync(resposta);

        using var documento = await JsonDocument.ParseAsync(
            await resposta.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!documento.RootElement.TryGetProperty("signedOperation", out var operacao)
            || operacao.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Resposta inválida do gateway ao obter operação assinada.");

        return new OperacaoAssinadaRegistradaV1Dto(operacao.Clone());
    }

    public async Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson)
    {
        var resposta = await httpClient.GetAsync($"/credenciais/{Uri.EscapeDataString(credencialJson)}/verificar");
        await LancarSeFalhaAsync(resposta);

        var dto = await resposta.Content.ReadFromJsonAsync<ResultadoVerificacaoGatewayDto>(OpcoesJson)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao verificar credencial.");

        return new ResultadoVerificacao(dto.Valido, dto.Motivo);
    }

    public async Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId)
    {
        var resposta = await httpClient.GetAsync($"/ativos/{Uri.EscapeDataString(assetId)}/historico");
        await LancarSeFalhaAsync(resposta);

        var eventos = await resposta.Content.ReadFromJsonAsync<List<EventoHistoricoGatewayDto>>(OpcoesJson)
            ?? [];

        return eventos
            .Select(e => new EstadoRegistro(e.Evento, e.OcorridoEm, e.DidResponsavel))
            .ToList();
    }

    public async Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credencialId)
    {
        var resposta = await httpClient.GetAsync($"/credenciais/{Uri.EscapeDataString(credencialId)}");
        await LancarSeFalhaAsync(resposta);

        var dto = await resposta.Content.ReadFromJsonAsync<CredencialCoCGatewayDto>(OpcoesJson)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao obter credencial CoC.");

        return new CredencialCoCRegistrada(dto.CredencialId, dto.AssetId, dto.Evento, dto.Did, dto.PayloadHashSha256, dto.Revogada);
    }

    private static async Task LancarSeFalhaAsync(HttpResponseMessage resposta)
    {
        if (resposta.IsSuccessStatusCode)
        {
            return;
        }

        var corpo = await resposta.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Falha ao chamar o gateway do ledger ({(int)resposta.StatusCode}): {corpo}");
    }

    private record DidDocumentoGatewayDto(
        string Did,
        string MetodoDid,
        bool Ativo,
        [property: JsonPropertyName("didEmissor")] string? DidEmissor,
        [property: JsonPropertyName("criadoEm")] DateTime? CriadoEm,
        [property: JsonPropertyName("ativadoEm")] DateTime? AtivadoEm);

    private record ResultadoVerificacaoGatewayDto(bool Valido, string? Motivo);

    private record CredencialEmitidaGatewayDto(
        [property: JsonPropertyName("credencialId")] string CredencialId);

    private record OperacaoAssinadaGatewayDto(
        [property: JsonPropertyName("operationId")] string OperationId);

    private record DocumentoRotacionadoGatewayDto(
        string? Id,
        string Did,
        int DocumentVersion,
        IReadOnlyList<string> Authentication);

    private record EventoHistoricoGatewayDto(
        [property: JsonPropertyName("assetId")] string AssetId,
        string Evento,
        [property: JsonPropertyName("ocorridoEm")] DateTime OcorridoEm,
        [property: JsonPropertyName("didResponsavel")] string DidResponsavel,
        [property: JsonPropertyName("credencialId")] string CredencialId);

    private record CredencialCoCGatewayDto(
        [property: JsonPropertyName("credencialId")] string CredencialId,
        [property: JsonPropertyName("assetId")] string AssetId,
        string Evento,
        string Did,
        [property: JsonPropertyName("payloadHashSha256")] string PayloadHashSha256,
        bool Revogada);
}
