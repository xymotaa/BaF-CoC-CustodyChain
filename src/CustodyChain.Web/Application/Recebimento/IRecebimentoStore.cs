namespace CustodyChain.Web.Application.Recebimento;

public interface IRecebimentoStore
{
    Task<ContextoRecebimento?> ObterContextoAsync(long movimentacaoId, long destinoId, CancellationToken cancellationToken);

    Task ConfirmarAsync(RecebimentoConfirmado recebimento, CancellationToken cancellationToken);

    Task RecusarAsync(RecebimentoRecusado recebimento, CancellationToken cancellationToken);
}

public sealed record ContextoRecebimento(
    long MovimentacaoId,
    long VestigioId,
    long ProcessoId,
    string AssetRef,
    string RotuloEvidencia,
    string DidOrigem,
    string DidDestino,
    string CredencialId,
    string RemessaOperationId,
    EstadoRetornoRecusa EstadoAposRecusa,
    string? NumeroLacreEsperado);

public enum EstadoRetornoRecusa
{
    Coletado,
    Recebido
}

public sealed record RecebimentoConfirmado(
    long MovimentacaoId,
    long DestinoId,
    string? NumeroLacreEsperado,
    string NumeroLacreConferido,
    bool LacreConfere,
    DateTime RecebidoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);

public sealed record RecebimentoRecusado(
    long MovimentacaoId,
    long DestinoId,
    string MotivoRecusa,
    EstadoRetornoRecusa EstadoAposRecusa,
    DateTime RecusadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);
