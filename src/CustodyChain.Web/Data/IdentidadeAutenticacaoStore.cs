using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class IdentidadeAutenticacaoStore(CustodyChainDbContext db) : IIdentidadeAutenticacaoStore
{
    public Task<IdentidadeAutenticada?> ObterAtivaAsync(
        string did,
        CancellationToken cancellationToken = default) =>
        db.Intervenientes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.Did == did && i.Situacao == SituacaoInterveniente.ATIVO)
            .Select(i => new IdentidadeAutenticada(
                i.Id,
                i.Nome,
                i.Did,
                i.Perfil.Codigo,
                i.Perfil.Nome))
            .FirstOrDefaultAsync(cancellationToken);
}
