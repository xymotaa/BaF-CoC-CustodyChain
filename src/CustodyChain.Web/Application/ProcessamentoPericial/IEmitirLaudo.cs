namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IEmitirLaudo
{
    Task<ResultadoEmissaoLaudo> ExecutarAsync(
        EmitirLaudoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EmitirLaudoCommand(long PeritoId, long PericiaId, string? Conteudo);

public sealed record ResultadoEmissaoLaudo(string RotuloEvidencia, string NumeroLaudo, bool AncoragemPendente);
