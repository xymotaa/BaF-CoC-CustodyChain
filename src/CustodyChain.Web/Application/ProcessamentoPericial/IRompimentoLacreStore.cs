namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IRompimentoLacreStore
{
    Task<ContextoRompimentoLacre?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken);

    Task PersistirAsync(RompimentoLacrePendente rompimento, CancellationToken cancellationToken);
}

public sealed record ContextoRompimentoLacre(
    long PericiaId,
    long VestigioId,
    string RotuloEvidencia,
    string DidPerito,
    bool CredencialValida,
    long ProcessoId,
    string? CredencialId,
    long? LacreId,
    string? NumeroLacre);

public sealed record RompimentoLacrePendente(
    long PericiaId,
    long PeritoId,
    long LacreId,
    string NumeroLacre,
    string Justificativa,
    DateTime RompidoEm,
    DateTime ConfirmadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);
