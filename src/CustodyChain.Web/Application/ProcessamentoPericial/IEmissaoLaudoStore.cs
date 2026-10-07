namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IEmissaoLaudoStore
{
    Task<ContextoEmissaoLaudo?> ObterContextoAsync(long periciaId, long peritoId, CancellationToken cancellationToken);

    Task PersistirAsync(LaudoPendente laudo, CancellationToken cancellationToken);
}

public sealed record ContextoEmissaoLaudo(
    long PericiaId,
    long VestigioId,
    long ProcessoId,
    string RotuloEvidencia,
    string DidPerito,
    string CredencialId,
    string? HashVestigios);

public sealed record LaudoPendente(
    long PericiaId,
    long PeritoId,
    string Numero,
    string Conteudo,
    string HashVestigios,
    string HashLaudo,
    DateTime EmitidoEm,
    DateTime ConfirmadoEm,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);
