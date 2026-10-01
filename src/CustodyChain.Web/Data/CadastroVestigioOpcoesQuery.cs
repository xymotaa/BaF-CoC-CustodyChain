using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class CadastroVestigioOpcoesQuery(CustodyChainDbContext db) : ICadastroVestigioOpcoesQuery
{
    public async Task<IReadOnlyList<OpcaoProcessoCadastroVestigio>> ListarProcessosAtivosAsync(CancellationToken cancellationToken = default) =>
        await db.Processos
            .Where(p => p.Situacao == SituacaoProcesso.ATIVO)
            .OrderBy(p => p.Numero)
            .Select(p => new OpcaoProcessoCadastroVestigio(p.Id, p.Numero, p.NomeOperacao))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OpcaoTipoCadastroVestigio>> ListarTiposAsync(CancellationToken cancellationToken = default) =>
        await db.TiposVestigio
            .OrderBy(t => t.Descricao)
            .Select(t => new OpcaoTipoCadastroVestigio(t.Id, t.Descricao, t.Categoria.ToString()))
            .ToListAsync(cancellationToken);
}
