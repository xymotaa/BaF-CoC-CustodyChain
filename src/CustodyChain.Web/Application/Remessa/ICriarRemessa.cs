namespace CustodyChain.Web.Application.Remessa;

public interface ICriarRemessa
{
    Task<ResultadoCriarRemessa> ExecutarAsync(
        CriarRemessaCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record CriarRemessaCommand(
    long CriadorId,
    long VestigioId,
    long DestinoId,
    DateTime DataHoraSaida,
    string? CodigoRastreamento);

public sealed record ResultadoCriarRemessa(
    string RotuloEvidencia,
    string NomeDestino,
    bool AncoragemPendente);
