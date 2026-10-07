using System.Text.Json;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IUnificarAmostras
{
    Task<PreparacaoUnificacaoAmostras> PrepararAsync(UnificarAmostrasCommand command, CancellationToken cancellationToken = default);
    Task<ResultadoUnificacaoAmostras> ExecutarAsync(ConcluirUnificacaoAmostrasCommand command, CancellationToken cancellationToken = default);
}

public sealed record UnificarAmostrasCommand(long PeritoId, long PericiaId, string? OutrosVestigiosOrigemIds,
    string? RotuloEvidenciaResultante, string? DescricaoResultante, string? Justificativa);

public sealed record ConcluirUnificacaoAmostrasCommand(
    long PeritoId,
    long PericiaId,
    string? OutrosVestigiosOrigemIds,
    string? RotuloEvidenciaResultante,
    string? DescricaoResultante,
    string? Justificativa,
    JsonElement OperacaoAssinada);

public sealed record PreparacaoUnificacaoAmostras(JsonElement Operacao, string DidPerito, int QuantidadeOrigens);

public sealed record ResultadoUnificacaoAmostras(int QuantidadeOrigens, string RotuloEvidenciaResultante, bool AncoragemPendente);
