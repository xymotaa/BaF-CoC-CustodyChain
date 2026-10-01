namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IFracionarAmostra
{
    Task<ResultadoFracionamentoAmostra> ExecutarAsync(
        FracionarAmostraCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record FracionarAmostraCommand(
    long PeritoId,
    long PericiaId,
    string? RotuloEvidenciaResultante,
    string? DescricaoResultante,
    string? QuantidadeDescrita,
    string? Justificativa);

public sealed record ResultadoFracionamentoAmostra(string RotuloEvidenciaOrigem, string RotuloEvidenciaResultante, bool AncoragemPendente);
