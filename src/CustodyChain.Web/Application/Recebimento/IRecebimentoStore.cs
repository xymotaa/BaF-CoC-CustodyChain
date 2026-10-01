namespace CustodyChain.Web.Application.Recebimento;

public interface IRecebimentoStore
{
    Task<ContextoRecebimento?> ObterContextoAsync(long movimentacaoId, long destinoId, CancellationToken cancellationToken);

    Task ConfirmarAsync(RecebimentoConfirmadoPendente recebimento, CancellationToken cancellationToken);

    Task RecusarAsync(RecebimentoRecusadoPendente recebimento, CancellationToken cancellationToken);
}

public sealed record ContextoRecebimento(
    long MovimentacaoId,
    long VestigioId,
    string RotuloEvidencia,
    string DidDestino,
    string? NumeroLacreEsperado);

public sealed record RecebimentoConfirmadoPendente(
    long MovimentacaoId,
    long DestinoId,
    string? NumeroLacreEsperado,
    string NumeroLacreConferido,
    bool LacreConfere,
    string Evento,
    DateTime RecebidoEm,
    string PayloadJson,
    string PayloadHashSha256,
    string CredencialId,
    string DidResponsavel);

public sealed record RecebimentoRecusadoPendente(
    long MovimentacaoId,
    long DestinoId,
    string MotivoRecusa,
    DateTime RecusadoEm,
    string PayloadJson,
    string PayloadHashSha256,
    string CredencialId,
    string DidResponsavel);
