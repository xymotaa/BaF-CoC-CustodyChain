namespace CustodyChain.Web.Application.Remessa;

public interface IRemessaOpcoesQuery
{
    Task<IReadOnlyList<OpcaoVestigioRemessa>> ListarVestigiosDisponiveisAsync(long criadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpcaoDestinoRemessa>> ListarDestinosDisponiveisAsync(long criadorId, CancellationToken cancellationToken = default);
}

public sealed record OpcaoVestigioRemessa(long Id, string RotuloEvidencia, string Descricao);
public sealed record OpcaoDestinoRemessa(long Id, string Nome, string Perfil);
