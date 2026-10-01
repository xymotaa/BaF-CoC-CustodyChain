namespace CustodyChain.Web.Application.Arquivo;

public interface IDarEntradaArquivo
{
    Task<ResultadoDarEntradaArquivo> ExecutarAsync(
        DarEntradaArquivoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record DarEntradaArquivoCommand(
    long RecebedorId,
    long VestigioId,
    string? Central,
    string? Posicao,
    DateOnly? PrazoGuardaAte);

public sealed record ResultadoDarEntradaArquivo(string RotuloEvidencia, bool AncoragemPendente);
