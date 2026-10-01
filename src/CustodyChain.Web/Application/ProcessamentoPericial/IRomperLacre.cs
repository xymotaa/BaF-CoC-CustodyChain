namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IRomperLacre
{
    Task<ResultadoRompimentoLacre> ExecutarAsync(
        RomperLacreCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record RomperLacreCommand(long PeritoId, long PericiaId, string? Justificativa);

public sealed record ResultadoRompimentoLacre(string RotuloEvidencia, string NumeroLacre, bool AncoragemPendente);
