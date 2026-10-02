namespace CustodyChain.Web.Application.DestinacaoFinal;

public interface ISolicitarDestinacao
{
    Task<ResultadoSolicitacaoDestinacao> ExecutarAsync(
        SolicitarDestinacaoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record SolicitarDestinacaoCommand(
    long SolicitanteId,
    long? VestigioId,
    string? Tipo,
    string? DidMagistrado,
    string? NomeArquivoAutorizacao,
    byte[]? ConteudoAutorizacao,
    string? Observacao);

public sealed record ResultadoSolicitacaoDestinacao(string RotuloEvidencia);
