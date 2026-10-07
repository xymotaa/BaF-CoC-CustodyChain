using System.Text.Json;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IRomperLacre
{
    Task<PreparacaoRompimentoLacre> PrepararAsync(
        RomperLacreCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoRompimentoLacre> ExecutarAsync(
        ConcluirRompimentoLacreCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record RomperLacreCommand(long PeritoId, long PericiaId, string? Justificativa);

public sealed record ConcluirRompimentoLacreCommand(
    long PeritoId,
    long PericiaId,
    string? Justificativa,
    JsonElement OperacaoAssinada);

public sealed record PreparacaoRompimentoLacre(
    JsonElement Operacao,
    string DidPerito,
    string RotuloEvidencia,
    string NumeroLacre);

public sealed record ResultadoRompimentoLacre(string RotuloEvidencia, string NumeroLacre, bool AncoragemPendente);
