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

    public async Task<string> EmitirCredencialPermissaoAsync(CredencialPermissaoDto dto)
    {
        var credencialId = $"cred-perm-{Guid.NewGuid():N}";
        var corpo = new { credencialId, dto.Did, dto.DidEmissor, dto.Perfil };

        var resposta = await httpClient.PostAsJsonAsync("/credenciais/permissao", corpo, OpcoesJson);
        await LancarSeFalhaAsync(resposta);

        return credencialId;
    }

    public async Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto)
    {
        var credencialId = $"cred-coc-{Guid.NewGuid():N}";
        var corpo = new
        {
            credencialId,
            assetId = dto.AssetId,
            dto.Evento,
            dto.Did,
            dto.PayloadHashSha256
        };

        var resposta = await httpClient.PostAsJsonAsync("/credenciais/coc", corpo, OpcoesJson);
        await LancarSeFalhaAsync(resposta);

        return credencialId;
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
