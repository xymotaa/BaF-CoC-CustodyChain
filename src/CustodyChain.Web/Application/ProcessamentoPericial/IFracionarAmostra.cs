using System.Text.Json;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IFracionarAmostra
{
    Task<PreparacaoFracionamentoAmostra> PrepararAsync(
        FracionarAmostraCommand command,
        CancellationToken cancellationToken = default);
    Task<ResultadoFracionamentoAmostra> ExecutarAsync(ConcluirFracionamentoAmostraCommand command, CancellationToken cancellationToken = default);
}

public sealed record FracionarAmostraCommand(
    long PeritoId,
    long PericiaId,
    string? RotuloEvidenciaResultante,
    string? DescricaoResultante,
    string? QuantidadeDescrita,
    string? Justificativa);
public sealed record ConcluirFracionamentoAmostraCommand(long PeritoId, long PericiaId, string? RotuloEvidenciaResultante, string? DescricaoResultante, string? QuantidadeDescrita, string? Justificativa, JsonElement OperacaoAssinada);
public sealed record PreparacaoFracionamentoAmostra(JsonElement Operacao, string DidPerito, string RotuloEvidenciaOrigem);

public sealed record ResultadoFracionamentoAmostra(string RotuloEvidenciaOrigem, string RotuloEvidenciaResultante, bool AncoragemPendente);
