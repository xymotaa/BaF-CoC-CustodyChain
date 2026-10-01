namespace CustodyChain.Web.Application.Recebimento;

public interface IConfirmarRecebimento
{
    Task<ResultadoConfirmarRecebimento> ExecutarAsync(
        ConfirmarRecebimentoCommand command,
        CancellationToken cancellationToken = default);
}

public interface IRecusarRecebimento
{
    Task<ResultadoRecusarRecebimento> ExecutarAsync(
        RecusarRecebimentoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record ConfirmarRecebimentoCommand(long DestinoId, long MovimentacaoId, string? NumeroLacreConferido);

public sealed record RecusarRecebimentoCommand(long DestinoId, long MovimentacaoId, string? MotivoRecusa);

public sealed record ResultadoConfirmarRecebimento(string RotuloEvidencia, bool LacreConfere, bool AncoragemPendente);

public sealed record ResultadoRecusarRecebimento(string RotuloEvidencia, bool AncoragemPendente);
