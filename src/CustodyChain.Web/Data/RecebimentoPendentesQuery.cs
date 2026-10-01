using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RecebimentoPendentesQuery(CustodyChainDbContext db) : IRecebimentoPendentesQuery
{
    public async Task<IReadOnlyList<ItemRecebimentoPendente>> ListarAsync(long destinoId, CancellationToken cancellationToken = default)
    {
        if (destinoId <= 0)
            return [];

        return await db.Movimentacoes
            .AsNoTracking()
            .Where(m => m.Situacao == SituacaoMovimentacao.PENDENTE && m.DestinoId == destinoId)
            .OrderBy(m => m.DataHoraSaida)
            .Select(m => new ItemRecebimentoPendente(
                m.Id,
                m.VestigioId,
                m.Vestigio.RotuloEvidencia,
                m.Vestigio.Descricao,
                m.Origem != null ? m.Origem.Nome : "—",
                m.DataHoraSaida,
                m.CodigoRastreamento,
                db.Lacres
                    .Where(l => l.VestigioId == m.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
                    .OrderByDescending(l => l.AplicadoEm)
                    .Select(l => l.Numero)
                    .FirstOrDefault() ?? string.Empty))
            .ToListAsync(cancellationToken);
    }
}
