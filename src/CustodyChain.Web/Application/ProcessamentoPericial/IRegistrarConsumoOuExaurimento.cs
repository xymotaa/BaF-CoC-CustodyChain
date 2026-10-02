namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IRegistrarConsumoOuExaurimento
{
    Task<ResultadoConsumoOuExaurimento> ExecutarAsync(
        RegistrarConsumoOuExaurimentoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record RegistrarConsumoOuExaurimentoCommand(
    long PeritoId,
    long PericiaId,
    string? Tipo,
    string? QuantidadeDescrita,
    string? Justificativa);

public sealed record ResultadoConsumoOuExaurimento(
    string Tipo,
    string RotuloEvidencia,
    bool AncoragemPendente);
