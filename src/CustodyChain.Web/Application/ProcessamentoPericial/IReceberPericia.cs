using System.Text.Json;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IReceberPericia
{
    Task<PreparacaoRecebimentoPericia> PrepararAsync(
        ReceberPericiaCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoRecebimentoPericia> ExecutarAsync(
        ConcluirRecebimentoPericiaCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record ReceberPericiaCommand(long PeritoId, long PericiaId);

public sealed record ConcluirRecebimentoPericiaCommand(
    long PeritoId,
    long PericiaId,
    JsonElement OperacaoAssinada);

public sealed record PreparacaoRecebimentoPericia(
    JsonElement Operacao,
    string DidPerito,
    string RotuloEvidencia);

public sealed record ResultadoRecebimentoPericia(string RotuloEvidencia);
