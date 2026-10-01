namespace CustodyChain.Web.Application.CadastroVestigio;

public interface ICadastroVestigioStore
{
    Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken);
    Task<bool> NumeroLacreExisteAsync(string numeroLacre, CancellationToken cancellationToken);
    Task<ProcessoCadastroVestigio?> ObterProcessoAtivoAsync(long processoId, CancellationToken cancellationToken);
    Task<bool> TipoVestigioExisteAsync(short tipoVestigioId, CancellationToken cancellationToken);
    Task<AtorCadastroVestigio?> ObterAtorAtivoAsync(long intervenienteId, CancellationToken cancellationToken);
    Task<long> PersistirAsync(CadastroVestigioPendente cadastro, CancellationToken cancellationToken);
}

public sealed record AtorCadastroVestigio(long Id, string Did);
public sealed record ProcessoCadastroVestigio(long Id, string Numero);

public sealed record CadastroVestigioPendente(
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
    string PayloadJson,
    string PayloadHashSha256,
    string CredencialId,
    string DidResponsavel);
