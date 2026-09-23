using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class ProcessamentoPericialController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
{
    [HttpGet("/processamento-pericial")]
    public async Task<IActionResult> Index()
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var agora = DateTime.UtcNow;

        var pericias = await db.Pericias
            .Include(p => p.Vestigio)
            .Include(p => p.Credencial)
            .Where(p => p.PeritoId == peritoId
                && (p.Situacao == SituacaoPericia.DESIGNADA || p.Situacao == SituacaoPericia.RECEBIDA))
            .OrderBy(p => p.SolicitadaEm)
            .ToListAsync();

        var itens = new List<ItemPericiaViewModel>();
        foreach (var p in pericias)
        {
            var lacreAtual = await db.Lacres
                .Where(l => l.VestigioId == p.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
                .OrderByDescending(l => l.AplicadoEm)
                .FirstOrDefaultAsync();

            // RN10: só perito com credencial de permissão vigente para
            // aquele vestígio específico pode romper o lacre.
            var credencialValida = p.Credencial is not null
                && p.Credencial.Situacao == SituacaoCredencial.VIGENTE
                && p.Credencial.VestigioId == p.VestigioId
                && (p.Credencial.ValidaAte is null || p.Credencial.ValidaAte > agora);

            itens.Add(new ItemPericiaViewModel(
                p.Id, p.VestigioId, p.Vestigio.RotuloEvidencia, p.Vestigio.Descricao,
                p.Situacao.ToString(), lacreAtual?.Numero, credencialValida));
        }

        return View(new ProcessamentoPericialListaViewModel { Pericias = itens });
    }

    [HttpPost("/processamento-pericial/receber")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receber(long periciaId)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .FirstOrDefaultAsync(p => p.Id == periciaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.DESIGNADA);

        if (pericia is null)
        {
            TempData["MensagemErro"] = "Perícia não encontrada ou já recebida.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        pericia.Situacao = SituacaoPericia.RECEBIDA;
        pericia.RecebidaEm = agora;
        pericia.Vestigio.CustodianteAtualId = peritoId;
        pericia.Vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Vestígio {pericia.Vestigio.RotuloEvidencia} recebido para perícia.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/romper-lacre")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RomperLacre(RomperLacreViewModel modelo)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .Include(p => p.Credencial)
            .FirstOrDefaultAsync(p => p.Id == modelo.PericiaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.RECEBIDA);

        if (pericia is null || string.IsNullOrWhiteSpace(modelo.Justificativa))
        {
            TempData["MensagemErro"] = "Perícia não encontrada, vestígio ainda não recebido, ou justificativa não informada.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;

        // RN10: só executa com credencial de permissão vigente para aquele
        // vestígio específico. Checagem repetida aqui (não só na listagem)
        // porque a situação da credencial pode ter mudado entre o GET e o
        // POST (ex: revogada nesse intervalo).
        var credencialValida = pericia.Credencial is not null
            && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
            && pericia.Credencial.VestigioId == pericia.VestigioId
            && (pericia.Credencial.ValidaAte is null || pericia.Credencial.ValidaAte > agora);

        if (!credencialValida)
        {
            TempData["MensagemErro"] = "Credencial de permissão inválida, revogada ou expirada para este vestígio (RN10).";
            return RedirectToAction(nameof(Index));
        }

        var lacreAtual = await db.Lacres
            .Where(l => l.VestigioId == pericia.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
            .OrderByDescending(l => l.AplicadoEm)
            .FirstOrDefaultAsync();

        if (lacreAtual is null)
        {
            TempData["MensagemErro"] = "Nenhum lacre intacto encontrado para este vestígio.";
            return RedirectToAction(nameof(Index));
        }

        lacreAtual.Situacao = SituacaoLacre.ROMPIDO;
        lacreAtual.RompidoPorId = peritoId;
        lacreAtual.RompidoEm = agora;
        lacreAtual.JustificativaRompimento = modelo.Justificativa.Trim();

        pericia.Situacao = SituacaoPericia.EM_EXECUCAO;
        pericia.Vestigio.Estado = EstadoVestigio.EmPericia;
        pericia.Vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        var perito = await db.Intervenientes.FindAsync(peritoId);
        var payload = new
        {
            RE = pericia.Vestigio.RotuloEvidencia,
            NumeroLacreRompido = lacreAtual.Numero,
            Justificativa = lacreAtual.JustificativaRompimento,
            RompidoPor = perito!.Did,
            RompidoEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        // Credencial #4 (Quadro 14): rompimento.
        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(pericia.VestigioId.ToString(), "ROMPIMENTO", perito.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = peritoId,
            EmissorId = peritoId,
            VestigioId = pericia.VestigioId,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "LACRE",
            RegistroOrigemId = lacreAtual.Id,
            VestigioId = pericia.VestigioId,
            Evento = "ROMPIMENTO",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Lacre {lacreAtual.Numero} rompido. Vestígio {pericia.Vestigio.RotuloEvidencia} liberado para exame.";
        return RedirectToAction(nameof(Index));
    }
}
