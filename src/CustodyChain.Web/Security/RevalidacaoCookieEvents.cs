using System.Security.Claims;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Security;

public sealed class RevalidacaoCookieEvents(
    CustodyChainDbContext db,
    IDidRegistry didRegistry,
    ILogger<RevalidacaoCookieEvents> logger) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var did = context.Principal?.FindFirstValue("did");
        if (string.IsNullOrWhiteSpace(did))
        {
            await RejeitarAsync(context);
            return;
        }

        try
        {
            var ativoLocal = await db.Intervenientes
                .AsNoTracking()
                .AnyAsync(i => i.Did == did && i.Situacao == SituacaoInterveniente.ATIVO);
            var documento = ativoLocal ? await didRegistry.ResolverAsync(did) : null;
            if (!ativoLocal || documento is null || !documento.Ativo
                || !string.Equals(documento.Status, "ATIVO", StringComparison.Ordinal))
            {
                await RejeitarAsync(context);
            }
        }
        catch (Exception erro)
        {
            logger.LogWarning(erro, "Falha ao revalidar no ledger a sessão do DID {Did}.", did);
            await RejeitarAsync(context);
        }
    }

    private static async Task RejeitarAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
