using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RemessaOpcoesQuery(CustodyChainDbContext db) : IRemessaOpcoesQuery
{
    public async Task<IReadOnlyList<OpcaoVestigioRemessa>> ListarVestigiosDisponiveisAsync(long criadorId, CancellationToken cancellationToken = default) =>
        await db.Vestigios
            .Where(v => (v.Estado == EstadoVestigio.Coletado || v.Estado == EstadoVestigio.Recebido)
                        && v.CustodianteAtualId == criadorId
                        && v.AssetRef != null)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioRemessa(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OpcaoDestinoRemessa>> ListarDestinosDisponiveisAsync(long criadorId, CancellationToken cancellationToken = default) =>
        await db.Intervenientes.AptosParaOperacoesLedger()
            .Include(i => i.Perfil)
            .Where(i => i.Situacao == SituacaoInterveniente.ATIVO
                        && i.Perfil.Codigo == "CUSTODIA"
                        && i.Id != criadorId)
            .OrderBy(i => i.Perfil.Nome)
            .Select(i => new OpcaoDestinoRemessa(i.Id, i.Nome, i.Perfil.Nome))
            .ToListAsync(cancellationToken);
}
