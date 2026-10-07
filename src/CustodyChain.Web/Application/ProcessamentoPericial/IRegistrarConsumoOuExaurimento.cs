using System.Text.Json;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IRegistrarConsumoOuExaurimento
{
    Task<PreparacaoConsumoOuExaurimento> PrepararAsync(
        RegistrarConsumoOuExaurimentoCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoConsumoOuExaurimento> ExecutarAsync(
        ConcluirConsumoOuExaurimentoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record RegistrarConsumoOuExaurimentoCommand(
    long PeritoId,
    long PericiaId,
    string? Tipo,
    string? QuantidadeDescrita,
    string? Justificativa);

public sealed record ConcluirConsumoOuExaurimentoCommand(
    long PeritoId,
    long PericiaId,
    string? Tipo,
    string? QuantidadeDescrita,
    string? Justificativa,
    JsonElement OperacaoAssinada);

public sealed record PreparacaoConsumoOuExaurimento(
    JsonElement Operacao,
    string DidPerito,
    string RotuloEvidencia,
    string Tipo);

public sealed record ResultadoConsumoOuExaurimento(
    string Tipo,
    string RotuloEvidencia,
    bool AncoragemPendente);
