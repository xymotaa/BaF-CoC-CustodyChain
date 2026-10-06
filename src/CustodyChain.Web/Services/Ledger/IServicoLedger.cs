namespace CustodyChain.Web.Services.Ledger;

public enum TipoAtor
{
    Admin,
    Custodian,
    Delegate,
    Expert,
    Judge,
    Prosecutor,
    Lawyer
}

public record DidDocument(string Did, string MetodoDid, bool Ativo);

public record RegistroDidPendenteDto(object Command, string Signature);

public record AtivacaoDidV2Dto(object Command, string KeyId, string Signature);

public record CredencialPermissaoDto(string Did, string DidEmissor, string Perfil);

public record CredencialPermissaoV2Dto(System.Text.Json.JsonElement Credential);

public record RevogacaoCredencialV2Dto(object Command, string KeyId, string Signature);

public record CredencialCoCDto(
    string AssetId,
    string Evento,
    string Did,
    string PayloadHashSha256,
    string? CredencialId = null);

public record ResultadoVerificacao(bool Valido, string? Motivo);

public record EstadoRegistro(string Estado, DateTime OcorridoEm, string DidResponsavel);

public record CredencialCoCRegistrada(string CredencialId, string AssetId, string Evento, string Did, string PayloadHashSha256, bool Revogada);

public interface IServicoLedger
{
    Task RegistrarDidV2PendenteAsync(RegistroDidPendenteDto dto, CancellationToken cancellationToken = default);
    Task AtivarDidV2Async(string did, AtivacaoDidV2Dto dto, CancellationToken cancellationToken = default);
    Task<string> GerarDidAsync(TipoAtor tipo);
    Task AtivarDidAsync(string did, string didEmissor, string senhaEmissor);
    Task<DidDocument> ResolverDidAsync(string did);
    Task<string> EmitirCredencialPermissaoAsync(CredencialPermissaoDto dto);
    Task<string> EmitirCredencialPermissaoV2Async(CredencialPermissaoV2Dto dto, CancellationToken cancellationToken = default);
    Task RevogarCredencialV2Async(string credencialId, RevogacaoCredencialV2Dto dto, CancellationToken cancellationToken = default);
    Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto, CancellationToken cancellationToken = default);
    Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson);
    Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId);
    Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credencialId);
}
