using System.Diagnostics;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class HomeController(CustodyChainDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var contagemPorEstado = await db.Vestigios
            .GroupBy(v => v.Estado)
            .Select(g => new ContagemEstadoViewModel(g.Key.ToString(), g.Count()))
            .ToListAsync();

        var modelo = new DashboardViewModel
        {
            TotalVestigios = contagemPorEstado.Sum(c => c.Quantidade),
            TotalProcessosAtivos = await db.Processos.CountAsync(p => p.Situacao == Models.Entities.SituacaoProcesso.ATIVO),
            RegistrosLedgerPendentes = await db.RegistrosLedger.CountAsync(r => r.Estado == Models.Entities.EstadoRegistroLedger.PENDENTE),
            ContagemPorEstado = contagemPorEstado
        };

        return View(modelo);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
