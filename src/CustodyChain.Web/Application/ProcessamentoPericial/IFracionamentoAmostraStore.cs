namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IFracionamentoAmostraStore
{
    Task<ContextoFracionamentoAmostra?> ObterContextoAsync(long periciaId, long peritoId, DateTime agora, CancellationToken cancellationToken);

    Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken);

    Task PersistirAsync(FracionamentoAmostraPendente fracionamento, CancellationToken cancellationToken);
}

public sealed record ContextoFracionamentoAmostra(
    long PericiaId,
    long VestigioOrigemId,
    string RotuloEvidenciaOrigem,
    string RotuloConjunto,
    long ProcessoId,
    short TipoVestigioId,
    string? HashSha256,
    string DidPerito,
    string CredencialId);

public sealed record FracionamentoAmostraPendente(
    long PericiaId,
    long PeritoId,
    string RotuloEvidenciaOrigem,
    string RotuloConjunto,
    long ProcessoId,
    short TipoVestigioId,
    string? HashSha256,
    string RotuloEvidenciaResultante,
    string DescricaoResultante,
    string? QuantidadeDescrita,
    string Justificativa,
    DateTime ExecutadoEm,
    DateTime ConfirmadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);
