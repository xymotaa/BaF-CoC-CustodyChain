namespace CustodyChain.Web.Application.Arquivo;

public interface IDarEntradaArquivo
{
    Task<PreparacaoEntradaArquivo> PrepararAsync(
        DarEntradaArquivoCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoDarEntradaArquivo> ExecutarAsync(
        ConcluirEntradaArquivoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record DarEntradaArquivoCommand(
    long RecebedorId,
    long VestigioId,
    string? Central,
    string? Posicao,
    DateOnly? PrazoGuardaAte);

public sealed record ConcluirEntradaArquivoCommand(DarEntradaArquivoCommand Entrada, System.Text.Json.JsonElement OperacaoAssinada);
public sealed record PreparacaoEntradaArquivo(System.Text.Json.JsonElement Operacao, string DidSignatario);
public sealed record ResultadoDarEntradaArquivo(string RotuloEvidencia, bool AncoragemPendente);
