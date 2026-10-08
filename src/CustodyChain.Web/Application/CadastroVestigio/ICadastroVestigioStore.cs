namespace CustodyChain.Web.Application.CadastroVestigio;

public interface ICadastroVestigioStore
{
    Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken);
    Task<bool> NumeroLacreExisteAsync(string numeroLacre, CancellationToken cancellationToken);
    Task<ProcessoCadastroVestigio?> ObterProcessoAtivoAsync(long processoId, CancellationToken cancellationToken);
    Task<bool> TipoVestigioExisteAsync(short tipoVestigioId, CancellationToken cancellationToken);
    Task<AtorCadastroVestigio?> ObterAtorAtivoAsync(long intervenienteId, long processoId, DateTime agora, CancellationToken cancellationToken);
    Task<long> PersistirAsync(CadastroVestigioPendente cadastro, CancellationToken cancellationToken);
}

public sealed record AtorCadastroVestigio(long Id, string Did, string CredencialId);
public sealed record ProcessoCadastroVestigio(long Id, string Numero);

public sealed record CadastroVestigioPendente(
    string AssetRef,
    string RotuloEvidencia,
    string RotuloConjunto,
    string? NumeroEvidencia,
    long ProcessoId,
    short TipoVestigioId,
    string Descricao,
    long CriadorId,
    string LocalColeta,
    DateTime DataHoraColeta,
    string? MetodoColeta,
    bool HouveIntercorrencia,
    string? DescricaoIntercorrencia,
    string NumeroLacre,
    DateTime CriadoEm,
    DateTime ConfirmadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel,
    IntegridadeEvidenciaColeta? Integridade);

public sealed record IntegridadeEvidenciaColeta(
    string Algorithm,
    string ContentHashSha256,
    string ContentCid,
    long ByteLength,
    string MediaType,
    string FileName);
