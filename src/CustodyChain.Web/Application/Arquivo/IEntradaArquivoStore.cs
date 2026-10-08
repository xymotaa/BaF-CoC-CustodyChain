namespace CustodyChain.Web.Application.Arquivo;

public interface IEntradaArquivoStore
{
    Task<ContextoEntradaArquivo?> ObterContextoAsync(long vestigioId, long recebedorId, CancellationToken cancellationToken);

    Task PersistirAsync(EntradaArquivoConfirmada entrada, CancellationToken cancellationToken);
}

public sealed record ContextoEntradaArquivo(
    long VestigioId,
    long ProcessoId,
    string AssetRef,
    string RotuloEvidencia,
    string DidRecebedor,
    string CredencialId,
    string RecebimentoOperationId);

public sealed record EntradaArquivoConfirmada(
    long VestigioId,
    long RecebedorId,
    string Central,
    string? Posicao,
    DateOnly? PrazoGuardaAte,
    DateTime EntradaEm,
    string RecebimentoOperationId,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaId,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);
