namespace CustodyChain.Web.Application.DestinacaoFinal;

public interface ISolicitarDestinacao
{
    Task<PreparacaoSolicitacaoDestinacao> PrepararAsync(
        PrepararSolicitacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoSolicitacaoDestinacao> ExecutarAsync(
        ConcluirSolicitacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record PrepararSolicitacaoDestinacaoCommand(
    long SolicitanteId,
    long? VestigioId,
    string? Tipo,
    string? DidMagistrado,
    string? NomeArquivoAutorizacao,
    byte[]? ConteudoAutorizacao,
    string? Observacao);

public sealed record ConcluirSolicitacaoDestinacaoCommand(
    long SolicitanteId,
    long VestigioId,
    System.Text.Json.JsonElement OperacaoAssinada);

public sealed record PreparacaoSolicitacaoDestinacao(
    System.Text.Json.JsonElement Operacao,
    string DidSignatario);

public sealed record ResultadoSolicitacaoDestinacao(
    string RotuloEvidencia,
    bool AncoragemPendente);
