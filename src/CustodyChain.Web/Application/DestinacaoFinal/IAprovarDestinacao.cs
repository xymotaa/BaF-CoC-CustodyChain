namespace CustodyChain.Web.Application.DestinacaoFinal;

public interface IAprovarDestinacao
{
    Task<PreparacaoAprovacaoDestinacao> PrepararAsync(
        PrepararAprovacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoAprovacaoDestinacao> ExecutarAsync(
        ConcluirAprovacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record PrepararAprovacaoDestinacaoCommand(long AprovadorId, long DescarteId);

public sealed record ConcluirAprovacaoDestinacaoCommand(
    long AprovadorId,
    long DescarteId,
    System.Text.Json.JsonElement OperacaoAssinada);

public sealed record PreparacaoAprovacaoDestinacao(
    System.Text.Json.JsonElement Operacao,
    string DidSignatario);

public sealed record ResultadoAprovacaoDestinacao(string Tipo, string RotuloEvidencia, bool AncoragemPendente);
