using System.Security.Claims;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

public class AutenticacaoController(CustodyChainDbContext db) : Controller
{
    [HttpGet("/entrar")]
    [AllowAnonymous]
    public async Task<IActionResult> Entrar()
    {
        var modelo = new LoginViewModel
        {
            CredenciaisDisponiveis = await CarregarCredenciaisAtivasAsync()
        };
        return View(modelo);
    }

    [HttpPost("/entrar")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrar(LoginViewModel modelo)
    {
        if (!ModelState.IsValid)
        {
            modelo.CredenciaisDisponiveis = await CarregarCredenciaisAtivasAsync();
            return View(modelo);
        }

        var interveniente = await db.Intervenientes
            .Include(i => i.Perfil)
            .FirstOrDefaultAsync(i => i.Did == modelo.Did && i.Situacao == SituacaoInterveniente.ATIVO);

        // Sem wallet real nesta fase: qualquer senha não vazia é aceita para
        // a credencial selecionada. A validação de senha de fato acontece no
        // dispositivo do titular contra a wallet cifrada (fora do servidor).
        if (interveniente is null)
        {
            ModelState.AddModelError(string.Empty, "Credencial não encontrada ou não ativa.");
            modelo.CredenciaisDisponiveis = await CarregarCredenciaisAtivasAsync();
            return View(modelo);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, interveniente.Id.ToString()),
            new(ClaimTypes.Name, interveniente.Nome),
            new("did", interveniente.Did),
            new(ClaimTypes.Role, interveniente.Perfil.Codigo),
            new("perfil_nome", interveniente.Perfil.Nome),
        };

        var identidade = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidade));

        return RedirectToAction("Index", "Home");
    }

    [HttpPost("/sair")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Entrar));
    }

    private async Task<List<OpcaoDidViewModel>> CarregarCredenciaisAtivasAsync() =>
        await db.Intervenientes
            .Include(i => i.Perfil)
            .Where(i => i.Situacao == SituacaoInterveniente.ATIVO)
            .OrderBy(i => i.Perfil.Nome)
            .Select(i => new OpcaoDidViewModel(i.Did, i.Nome, i.Perfil.Nome))
            .ToListAsync();
}
