namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IConsumoOuExaurimentoStore
{
    Task<ContextoConsumoOuExaurimento?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        CancellationToken cancellationToken);

    Task PersistirAsync(
        ConsumoOuExaurimentoPendente operacao,
        CancellationToken cancellationToken);
}

public sealed record ContextoConsumoOuExaurimento(
    long PericiaId,
    long VestigioId,
    string RotuloEvidencia,
    string DidPerito);

public sealed record ConsumoOuExaurimentoPendente(
    long PericiaId,
    long PeritoId,
    long VestigioId,
    string Tipo,
    string? QuantidadeDescrita,
    string Justificativa,
    DateTime ExecutadoEm,
    string PayloadJson,
    string PayloadHashSha256,
    string CredencialId,
    string DidResponsavel);
