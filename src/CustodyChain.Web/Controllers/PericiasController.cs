using System.Security.Claims;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class PericiasController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
{
    [HttpGet("/pericias/designar")]
    public async Task<IActionResult> Designar()
    {
        var modelo = new DesignarPericiaViewModel();
        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/pericias/designar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Designar(DesignarPericiaViewModel modelo)
    {
        var designadorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var vestigio = modelo.VestigioId is not null
            ? await db.Vestigios.FirstOrDefaultAsync(v =>
                v.Id == modelo.VestigioId && v.Estado == EstadoVestigio.Armazenado)
            : null;

        if (modelo.VestigioId is not null && vestigio is null)
        {
            ModelState.AddModelError(nameof(modelo.VestigioId), "Vestígio não encontrado ou não está mais no estado Armazenado.");
        }

        if (!Enum.TryParse<PrioridadePericia>(modelo.Prioridade, out var prioridade))
        {
            ModelState.AddModelError(nameof(modelo.Prioridade), "Prioridade inválida.");
        }

        if (!ModelState.IsValid || vestigio is null)
        {
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }

        var agora = DateTime.UtcNow;
        var perito = await db.Intervenientes.FindAsync(modelo.PeritoId!.Value);
        var designador = await db.Intervenientes.FindAsync(designadorId);

        // RN10: o rompimento de lacre exige credencial de permissão válida
        // e não revogada para aquele vestígio específico. É emitida aqui,
        // no momento da designação — sem ela o perito nunca teria como
        // satisfazer a regra na hora de examinar o item.
        var credencialId = await ledger.EmitirCredencialPermissaoAsync(
            new CredencialPermissaoDto(perito!.Did, designador!.Did, "PERITO"));

        var credencial = new Credencial
        {
            Tipo = TipoCredencial.PERMISSAO,
            Identificador = credencialId,
            TitularId = perito.Id,
            EmissorId = designadorId,
            ProcessoId = vestigio.ProcessoId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        };
        db.Credenciais.Add(credencial);
        await db.SaveChangesAsync();

        db.Pericias.Add(new Pericia
        {
            VestigioId = vestigio.Id,
            ProcessoId = vestigio.ProcessoId,
            PeritoId = perito.Id,
            CredencialId = credencial.Id,
            AreaPericial = modelo.AreaPericial,
            Prioridade = prioridade,
            SolicitadaEm = agora,
            Situacao = SituacaoPericia.DESIGNADA,
        });

        vestigio.Estado = EstadoVestigio.EmPericia;
        vestigio.EtapaAtual = 8; // Processamento, art. 158-B
        vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Perícia do vestígio {vestigio.RotuloEvidencia} designada a {perito.Nome}.";
        return RedirectToAction(nameof(Designar));
    }

    private async Task CarregarOpcoesAsync(DesignarPericiaViewModel modelo)
    {
        modelo.VestigiosDisponiveis = await db.Vestigios
            .Where(v => v.Estado == EstadoVestigio.Armazenado)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync();

        modelo.PeritosDisponiveis = await db.Intervenientes
            .Include(i => i.Perfil)
            .Where(i => i.Situacao == SituacaoInterveniente.ATIVO && i.Perfil.Codigo == "PERITO")
            .OrderBy(i => i.Nome)
            .Select(i => new OpcaoIntervenienteViewModel(i.Id, i.Nome, i.Perfil.Nome))
            .ToListAsync();
    }
}
