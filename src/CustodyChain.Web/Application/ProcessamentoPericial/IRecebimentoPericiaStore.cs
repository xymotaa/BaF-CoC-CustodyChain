namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IRecebimentoPericiaStore
{
    Task<ContextoRecebimentoPericia?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken);

    Task PersistirAsync(RecebimentoPericiaConfirmado recebimento, CancellationToken cancellationToken);
}

public sealed record ContextoRecebimentoPericia(
    long PericiaId,
    long VestigioId,
    long ProcessoId,
    string RotuloEvidencia,
    string DidPerito,
    string CredencialId);

public sealed record RecebimentoPericiaConfirmado(
    long PericiaId,
    long PeritoId,
    DateTime RecebidoEm,
    DateTime ConfirmadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);
