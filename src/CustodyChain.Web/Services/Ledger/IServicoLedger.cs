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

public record CredencialPermissaoDto(string Did, string DidEmissor, string Perfil);

public record CredencialCoCDto(string AssetId, string Evento, string Did, string PayloadHashSha256);

public record ResultadoVerificacao(bool Valido, string? Motivo);

public record EstadoRegistro(string Estado, DateTime OcorridoEm, string DidResponsavel);

public interface IServicoLedger
{
    Task<string> GerarDidAsync(TipoAtor tipo);
    Task AtivarDidAsync(string did, string didEmissor, string senhaEmissor);
    Task<DidDocument> ResolverDidAsync(string did);
    Task<string> EmitirCredencialPermissaoAsync(CredencialPermissaoDto dto);
    Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto);
    Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson);
    Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId);
}
