namespace CustodyChain.Web.Application.DestinacaoFinal;

public interface IAprovarDestinacao
{
    Task<ResultadoAprovacaoDestinacao> ExecutarAsync(
        AprovarDestinacaoCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record AprovarDestinacaoCommand(long AprovadorId, long DescarteId);

public sealed record ResultadoAprovacaoDestinacao(string Tipo, string RotuloEvidencia, bool AncoragemPendente);
