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
                && (p.Situacao == SituacaoPericia.DESIGNADA
                    || p.Situacao == SituacaoPericia.RECEBIDA
                    || p.Situacao == SituacaoPericia.EM_EXECUCAO))
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

    [HttpPost("/processamento-pericial/emitir-laudo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmitirLaudo(EmitirLaudoViewModel modelo)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // RN08: só peritos emitem laudos — garantido aqui porque a ação só
        // é alcançável por uma perícia designada ao perito autenticado.
        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .FirstOrDefaultAsync(p => p.Id == modelo.PericiaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.EM_EXECUCAO);

        if (pericia is null || string.IsNullOrWhiteSpace(modelo.Conteudo))
        {
            TempData["MensagemErro"] = "Perícia não encontrada, lacre ainda não rompido, ou conteúdo do laudo não informado.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        var vestigio = pericia.Vestigio;

        if (string.IsNullOrEmpty(vestigio.HashSha256))
        {
            TempData["MensagemErro"] = "Vestígio sem hash SHA-256 registrado (RN13) — não é possível vincular o laudo.";
            return RedirectToAction(nameof(Index));
        }

        // RN13: o laudo se vincula ao hash dos vestígios que fundamentaram
        // a análise. Esta perícia cobre um único vestígio, então o vínculo
        // é o próprio hash gravado na coleta (T-03).
        var hashVestigios = vestigio.HashSha256;

        var conteudo = modelo.Conteudo.Trim();
        var hashLaudo = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo + hashVestigios)));

        var numero = $"LAUDO-{agora:yyyy}-{pericia.Id:D6}";

        var laudo = new Laudo
        {
            PericiaId = pericia.Id,
            Numero = numero,
            Versao = 1,
            Conteudo = conteudo,
            HashVestigios = hashVestigios,
            HashLaudo = hashLaudo,
            // Assinatura Ed25519 real depende da wallet do titular, ainda
            // não implementada (pendência P-02 do documento de contexto,
            // classificada como evolução do MVP). Fica nula por ora.
            AssinaturaEd25519 = null,
            AssinadoPorId = peritoId,
            AssinadoEm = agora,
        };
        db.Laudos.Add(laudo);

        pericia.Situacao = SituacaoPericia.CONCLUIDA;
        pericia.ConcluidaEm = agora;
        vestigio.Estado = EstadoVestigio.Periciado;
        vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        var perito = await db.Intervenientes.FindAsync(peritoId);
        var payload = new
        {
            RE = vestigio.RotuloEvidencia,
            NumeroLaudo = laudo.Numero,
            Versao = laudo.Versao,
            HashVestigios = hashVestigios,
            HashLaudo = hashLaudo,
            EmitidoPor = perito!.Did,
            EmitidoEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        // Credencial #5 (Quadro 14): laudo.
        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), "LAUDO", perito.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = peritoId,
            EmissorId = peritoId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "LAUDO",
            RegistroOrigemId = laudo.Id,
            VestigioId = vestigio.Id,
            Evento = "LAUDO",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Laudo {laudo.Numero} emitido para o vestígio {vestigio.RotuloEvidencia}.";
        return RedirectToAction(nameof(Index));
    }
}
