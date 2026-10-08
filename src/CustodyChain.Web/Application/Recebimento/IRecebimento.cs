namespace CustodyChain.Web.Application.Recebimento;

public interface IConfirmarRecebimento
{
    Task<PreparacaoRecebimento> PrepararAsync(
        ConfirmarRecebimentoCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoConfirmarRecebimento> ExecutarAsync(
        ConcluirConfirmarRecebimentoCommand command,
        CancellationToken cancellationToken = default);
}

public interface IRecusarRecebimento
{
    Task<PreparacaoRecebimento> PrepararAsync(
        RecusarRecebimentoCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoRecusarRecebimento> ExecutarAsync(
        ConcluirRecusarRecebimentoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record ConfirmarRecebimentoCommand(long DestinoId, long MovimentacaoId, string? NumeroLacreConferido);
public sealed record ConcluirConfirmarRecebimentoCommand(ConfirmarRecebimentoCommand Recebimento, System.Text.Json.JsonElement OperacaoAssinada);

public sealed record RecusarRecebimentoCommand(long DestinoId, long MovimentacaoId, string? MotivoRecusa);
public sealed record ConcluirRecusarRecebimentoCommand(RecusarRecebimentoCommand Recusa, System.Text.Json.JsonElement OperacaoAssinada);

public sealed record PreparacaoRecebimento(System.Text.Json.JsonElement Operacao, string DidSignatario);

public sealed record ResultadoConfirmarRecebimento(string RotuloEvidencia, bool LacreConfere, bool AncoragemPendente);

public sealed record ResultadoRecusarRecebimento(string RotuloEvidencia, bool AncoragemPendente);
