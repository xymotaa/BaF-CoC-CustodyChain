namespace CustodyChain.Web.Application.Arquivo;

public interface IEntradaArquivoStore
{
    Task<ContextoEntradaArquivo?> ObterContextoAsync(long vestigioId, long recebedorId, CancellationToken cancellationToken);

    Task PersistirAsync(EntradaArquivoPendente entrada, CancellationToken cancellationToken);
}

public sealed record ContextoEntradaArquivo(long VestigioId, string RotuloEvidencia, string DidRecebedor);

public sealed record EntradaArquivoPendente(
    long VestigioId,
    long RecebedorId,
    string Central,
    string? Posicao,
    DateOnly? PrazoGuardaAte,
    DateTime EntradaEm,
    string PayloadJson,
    string PayloadHashSha256,
    string CredencialId,
    string DidResponsavel);
