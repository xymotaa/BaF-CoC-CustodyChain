using System.Text.Json;

namespace CustodyChain.Web.Services.Ledger;

/// <summary>
/// Escolhe, apenas no servidor, o gateway cuja identidade Fabric representa a
/// organização do DID que assinou a escrita. Nunca aceita MSP do cliente nem
/// redireciona silenciosamente para outra organização.
/// </summary>
public sealed class ServicoLedgerPorOrganizacao(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IServicoLedger
{
    public Task RegistrarDidV2PendenteAsync(RegistroDidPendenteDto dto, CancellationToken cancellationToken = default) =>
        Administrador().RegistrarDidV2PendenteAsync(dto, cancellationToken);

    public Task AtivarDidV2Async(string did, AtivacaoDidV2Dto dto, CancellationToken cancellationToken = default) =>
        Administrador().AtivarDidV2Async(did, dto, cancellationToken);

    public Task<DocumentoDidRotacionadoDto> RotacionarChaveDidV2Async(
        string did, RotacaoChaveDidV2Dto dto, CancellationToken cancellationToken = default) =>
        ParaDid(did).RotacionarChaveDidV2Async(did, dto, cancellationToken);

    public Task<DocumentoDidRotacionadoDto> RecuperarChaveDidV2Async(string did, RecuperacaoChaveDidV2Dto dto, CancellationToken cancellationToken = default) =>
        Administrador().RecuperarChaveDidV2Async(did, dto, cancellationToken);

    public Task<string> GerarDidAsync(TipoAtor tipo) => Administrador().GerarDidAsync(tipo);

    public Task AtivarDidAsync(string did, string didEmissor, string senhaEmissor) =>
        Administrador().AtivarDidAsync(did, didEmissor, senhaEmissor);

    public Task<DidDocument> ResolverDidAsync(string did) => Leitura().ResolverDidAsync(did);

    public Task<string> EmitirCredencialPermissaoV2Async(
        CredencialPermissaoV2Dto dto, CancellationToken cancellationToken = default) =>
        ParaDid(ObterDid(dto.Credential, "issuer")).EmitirCredencialPermissaoV2Async(dto, cancellationToken);

    public Task RevogarCredencialV2Async(
        string credencialId, RevogacaoCredencialV2Dto dto, CancellationToken cancellationToken = default) =>
        ParaDid(ObterDid(dto.Command, "issuerDid")).RevogarCredencialV2Async(credencialId, dto, cancellationToken);

    public Task<string> RegistrarOperacaoAssinadaV1Async(
        OperacaoAssinadaV1Dto dto, CancellationToken cancellationToken = default) =>
        ParaDid(ObterDid(dto.Operation, "signerDid")).RegistrarOperacaoAssinadaV1Async(dto, cancellationToken);

    public Task<OperacaoAssinadaRegistradaV1Dto> ObterOperacaoAssinadaV1Async(
        string operationId, CancellationToken cancellationToken = default) =>
        Leitura().ObterOperacaoAssinadaV1Async(operationId, cancellationToken);

    public Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson) =>
        Leitura().VerificarCredencialAsync(credencialJson);

    public Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId) =>
        Leitura().HistoricoRegistroAsync(assetId);

    public Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credencialId) =>
        Leitura().ObterCredencialCoCAsync(credencialId);

    private ServicoLedgerFabric Administrador() => ParaMsp(OrganizacaoFabricDid.Org1Msp);

    private ServicoLedgerFabric Leitura() => Administrador();

    private ServicoLedgerFabric ParaDid(string did) => ParaMsp(OrganizacaoFabricDid.ResolverMsp(did));

    private ServicoLedgerFabric ParaMsp(string mspId)
    {
        var gatewayUrl = ObterGatewayUrl(mspId);
        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            throw new InvalidOperationException(
                $"Gateway Fabric para {mspId} não configurado. A operação não será enviada por outra organização.");
        }

        return new ServicoLedgerFabric(httpClientFactory.CreateClient(NomeCliente(mspId)));
    }

    private string? ObterGatewayUrl(string mspId) => mspId switch
    {
        OrganizacaoFabricDid.Org1Msp => configuration["Ledger:Organizations:Org1MSP:GatewayUrl"]
            ?? configuration["Ledger:GatewayUrl"],
        OrganizacaoFabricDid.Org2Msp => configuration["Ledger:Organizations:Org2MSP:GatewayUrl"],
        _ => null
    };

    public static string NomeCliente(string mspId) => $"LedgerFabric:{mspId}";

    private static string ObterDid(JsonElement envelope, string propriedade)
    {
        if (envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty(propriedade, out var did)
            || did.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(did.GetString()))
        {
            throw new InvalidOperationException($"Envelope sem DID obrigatório para roteamento: {propriedade}.");
        }

        return did.GetString()!;
    }

    private static string ObterDid(object command, string propriedade)
    {
        var envelope = JsonSerializer.SerializeToElement(command);
        return ObterDid(envelope, propriedade);
    }
}
