namespace CustodyChain.Web.Application.Remessa;

public interface ICriarRemessaStore
{
    Task<ContextoRemessa?> ObterContextoAsync(
        long vestigioId,
        long criadorId,
        long destinoId,
        CancellationToken cancellationToken);

    Task PersistirAsync(RemessaConfirmada remessa, CancellationToken cancellationToken);
}

public sealed record ContextoRemessa(
    long VestigioId,
    string AssetRef,
    long ProcessoId,
    string RotuloEvidencia,
    string DidOrigem,
    string DidDestino,
    string NomeDestino,
    TipoTransferenciaRemessa TipoTransferencia,
    string? CredencialId,
    string? ColetaOperationId);

public enum TipoTransferenciaRemessa
{
    INICIAL,
    CUSTODIA
}

public sealed record RemessaConfirmada(
    long VestigioId,
    long CriadorId,
    long DestinoId,
    DateTime DataHoraSaida,
    string? CodigoRastreamento,
    DateTime CriadoEm,
    DateTime ConfirmadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel,
    TipoTransferenciaRemessa TipoTransferencia);
