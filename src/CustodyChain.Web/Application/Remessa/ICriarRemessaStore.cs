namespace CustodyChain.Web.Application.Remessa;

public interface ICriarRemessaStore
{
    Task<ContextoRemessa?> ObterContextoAsync(
        long vestigioId,
        long criadorId,
        long destinoId,
        CancellationToken cancellationToken);

    Task PersistirAsync(RemessaPendente remessa, CancellationToken cancellationToken);
}

public sealed record ContextoRemessa(
    long VestigioId,
    string RotuloEvidencia,
    string DidOrigem,
    string DidDestino,
    string NomeDestino);

public sealed record RemessaPendente(
    long VestigioId,
    long CriadorId,
    long DestinoId,
    DateTime DataHoraSaida,
    string? CodigoRastreamento,
    DateTime CriadoEm,
    string PayloadJson,
    string PayloadHashSha256,
    string CredencialId,
    string DidResponsavel);
