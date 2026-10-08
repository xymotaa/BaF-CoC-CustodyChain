using System.Text.Json;

namespace CustodyChain.Web.Application.Remessa;

public interface ICriarRemessa
{
    Task<PreparacaoRemessa> PrepararAsync(
        CriarRemessaCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoCriarRemessa> ExecutarAsync(
        ConcluirRemessaCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record CriarRemessaCommand(
    long CriadorId,
    long VestigioId,
    long DestinoId,
    DateTime DataHoraSaida,
    string? CodigoRastreamento);

public sealed record ConcluirRemessaCommand(CriarRemessaCommand Remessa, JsonElement OperacaoAssinada);
public sealed record PreparacaoRemessa(JsonElement Operacao, string DidColetor);

public sealed record ResultadoCriarRemessa(
    string RotuloEvidencia,
    string NomeDestino,
    bool AncoragemPendente);
