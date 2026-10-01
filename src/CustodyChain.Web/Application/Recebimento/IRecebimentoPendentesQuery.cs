namespace CustodyChain.Web.Application.Recebimento;

public interface IRecebimentoPendentesQuery
{
    Task<IReadOnlyList<ItemRecebimentoPendente>> ListarAsync(long destinoId, CancellationToken cancellationToken = default);
}

public sealed record ItemRecebimentoPendente(
    long MovimentacaoId,
    long VestigioId,
    string RotuloEvidencia,
    string Descricao,
    string OrigemNome,
    DateTime? DataHoraSaida,
    string? CodigoRastreamento,
    string NumeroLacreEsperado);
