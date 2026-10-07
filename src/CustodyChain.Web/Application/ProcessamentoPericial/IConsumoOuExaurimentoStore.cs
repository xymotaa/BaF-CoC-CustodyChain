namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IConsumoOuExaurimentoStore
{
    Task<ContextoConsumoOuExaurimento?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken);

    Task PersistirAsync(
        ConsumoOuExaurimentoPendente operacao,
        CancellationToken cancellationToken);
}

public sealed record ContextoConsumoOuExaurimento(
    long PericiaId,
    long VestigioId,
    long ProcessoId,
    string RotuloEvidencia,
    string DidPerito,
    string CredencialId);

public sealed record ConsumoOuExaurimentoPendente(
    long PericiaId,
    long PeritoId,
    long VestigioId,
    string Tipo,
    string? QuantidadeDescrita,
    string Justificativa,
    DateTime ExecutadoEm,
    DateTime ConfirmadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);
