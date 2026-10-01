namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IUnificarAmostras
{
    Task<ResultadoUnificacaoAmostras> ExecutarAsync(UnificarAmostrasCommand command, CancellationToken cancellationToken = default);
}

public sealed record UnificarAmostrasCommand(long PeritoId, long PericiaId, string? OutrosVestigiosOrigemIds,
    string? RotuloEvidenciaResultante, string? DescricaoResultante, string? Justificativa);

public sealed record ResultadoUnificacaoAmostras(int QuantidadeOrigens, string RotuloEvidenciaResultante, bool AncoragemPendente);
