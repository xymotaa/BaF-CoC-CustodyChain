using System.Text.Json;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IEmitirLaudo
{
    Task<PreparacaoEmissaoLaudo> PrepararAsync(
        EmitirLaudoCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoEmissaoLaudo> ExecutarAsync(
        ConcluirEmissaoLaudoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EmitirLaudoCommand(long PeritoId, long PericiaId, string? Conteudo);

public sealed record ConcluirEmissaoLaudoCommand(
    long PeritoId,
    long PericiaId,
    string? Conteudo,
    JsonElement OperacaoAssinada);

public sealed record PreparacaoEmissaoLaudo(
    JsonElement Operacao,
    string DidPerito,
    string RotuloEvidencia,
    string NumeroLaudo);

public sealed record ResultadoEmissaoLaudo(string RotuloEvidencia, string NumeroLaudo, bool AncoragemPendente);
