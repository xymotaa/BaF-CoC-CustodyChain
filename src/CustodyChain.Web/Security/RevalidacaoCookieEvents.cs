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
        var keyId = context.Principal?.FindFirstValue("did_key_id");
        var versaoValida = int.TryParse(
            context.Principal?.FindFirstValue("did_document_version"), out var documentVersion);
        if (string.IsNullOrWhiteSpace(did) || string.IsNullOrWhiteSpace(keyId) || !versaoValida)
        {
            await RejeitarAsync(context);
            return;
        }

        try
        {
            var ativoLocal = await db.Intervenientes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(i => i.Did == did && i.Situacao == SituacaoInterveniente.ATIVO);
            var documento = ativoLocal ? await didRegistry.ResolverAsync(did) : null;
            if (!ativoLocal || documento is null || !documento.Ativo
                || !string.Equals(documento.Status, "ATIVO", StringComparison.Ordinal)
                || documento.DocumentVersion != documentVersion
                || !documento.Authentication.Contains(keyId, StringComparer.Ordinal))
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
