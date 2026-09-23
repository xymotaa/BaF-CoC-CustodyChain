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
public class RecebimentoController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
{
    [HttpGet("/recebimento")]
    public async Task<IActionResult> Index()
    {
        var destinoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var pendentes = await db.Movimentacoes
            .Include(m => m.Vestigio)
            .Include(m => m.Origem)
            .Where(m => m.Situacao == SituacaoMovimentacao.PENDENTE && m.DestinoId == destinoId)
            .OrderBy(m => m.DataHoraSaida)
            .ToListAsync();

        var itens = new List<ItemRecebimentoViewModel>();
        foreach (var m in pendentes)
        {
            var lacreAtual = await db.Lacres
                .Where(l => l.VestigioId == m.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
                .OrderByDescending(l => l.AplicadoEm)
                .FirstOrDefaultAsync();

            itens.Add(new ItemRecebimentoViewModel(
                m.Id, m.VestigioId, m.Vestigio.RotuloEvidencia, m.Vestigio.Descricao,
                m.Origem?.Nome ?? "—", m.DataHoraSaida, m.CodigoRastreamento,
                lacreAtual?.Numero ?? ""));
        }

        return View(new RecebimentoListaViewModel { Pendentes = itens });
    }

    [HttpPost("/recebimento/confirmar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmar(ConfirmarRecebimentoViewModel modelo)
    {
        var destinoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var agora = DateTime.UtcNow;

        var movimentacao = await db.Movimentacoes
            .Include(m => m.Vestigio)
            .FirstOrDefaultAsync(m => m.Id == modelo.MovimentacaoId
                && m.Situacao == SituacaoMovimentacao.PENDENTE && m.DestinoId == destinoId);

        if (movimentacao is null || string.IsNullOrWhiteSpace(modelo.NumeroLacreConferido))
        {
            TempData["MensagemErro"] = "Recebimento não encontrado ou número do lacre não informado.";
            return RedirectToAction(nameof(Index));
        }

        var lacreEsperado = await db.Lacres
            .Where(l => l.VestigioId == movimentacao.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
            .OrderByDescending(l => l.AplicadoEm)
            .FirstOrDefaultAsync();

        var vestigio = movimentacao.Vestigio;
        var lacreConfere = lacreEsperado is not null && lacreEsperado.Numero == modelo.NumeroLacreConferido.Trim();

        movimentacao.DataHoraChegada = agora;
        movimentacao.CondicoesAdequadas = lacreConfere;
        // Segregação de funções (RN16): quem cria a remessa é a origem; quem
        // aprova o recebimento é sempre o destino, nunca a mesma pessoa.
        movimentacao.AprovadoPorId = destinoId;

        string evento;
        string payloadExtra;

        if (lacreConfere)
        {
            movimentacao.Situacao = SituacaoMovimentacao.ACEITA;
            vestigio.Estado = EstadoVestigio.Recebido;
            vestigio.EtapaAtual = 7;
            vestigio.CustodianteAtualId = destinoId;
            vestigio.AtualizadoEm = agora;
            evento = "RECEBIMENTO";
            payloadExtra = "lacre_conferido";
        }
        else
        {
            // RN21: divergência de lacre leva a CustodiaComprometida,
            // registrada e não revertida em silêncio.
            movimentacao.Situacao = SituacaoMovimentacao.ACEITA;
            vestigio.Estado = EstadoVestigio.CustodiaComprometida;
            vestigio.EtapaAtual = 7;
            vestigio.CustodianteAtualId = destinoId;
            vestigio.AtualizadoEm = agora;
            evento = "ROMPIMENTO";
            payloadExtra = "divergencia_lacre";
        }

        await db.SaveChangesAsync();

        var destino = await db.Intervenientes.FindAsync(destinoId);
        var payload = new
        {
            RE = vestigio.RotuloEvidencia,
            NumeroLacreEsperado = lacreEsperado?.Numero,
            NumeroLacreConferido = modelo.NumeroLacreConferido.Trim(),
            Resultado = payloadExtra,
            Recebedor = destino!.Did,
            RecebidoEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), evento, destino.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = destinoId,
            EmissorId = destinoId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "MOVIMENTACAO",
            RegistroOrigemId = movimentacao.Id,
            VestigioId = vestigio.Id,
            Evento = evento,
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = lacreConfere
            ? $"Vestígio {vestigio.RotuloEvidencia} recebido. Lacre conferido."
            : $"Divergência no lacre do vestígio {vestigio.RotuloEvidencia}. Custódia marcada como comprometida.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/recebimento/recusar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recusar(RecusarRecebimentoViewModel modelo)
    {
        var destinoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var agora = DateTime.UtcNow;

        var movimentacao = await db.Movimentacoes
            .Include(m => m.Vestigio)
            .FirstOrDefaultAsync(m => m.Id == modelo.MovimentacaoId
                && m.Situacao == SituacaoMovimentacao.PENDENTE && m.DestinoId == destinoId);

        if (movimentacao is null || string.IsNullOrWhiteSpace(modelo.MotivoRecusa))
        {
            TempData["MensagemErro"] = "Recebimento não encontrado ou motivo da recusa não informado.";
            return RedirectToAction(nameof(Index));
        }

        // RN19: a recusa reverte o vestígio ao estado anterior e remove o
        // solicitante (a movimentação some da fila de destino) — nada fica
        // em limbo. A custódia volta para quem originou a remessa.
        movimentacao.Situacao = SituacaoMovimentacao.RECUSADA;
        movimentacao.MotivoRecusa = modelo.MotivoRecusa.Trim();
        movimentacao.DataHoraChegada = agora;
        movimentacao.AprovadoPorId = destinoId;

        var vestigio = movimentacao.Vestigio;
        vestigio.Estado = EstadoVestigio.Coletado;
        vestigio.CustodianteAtualId = movimentacao.OrigemId;
        vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        var destino = await db.Intervenientes.FindAsync(destinoId);
        var payload = new
        {
            RE = vestigio.RotuloEvidencia,
            MotivoRecusa = movimentacao.MotivoRecusa,
            RecusadoPor = destino!.Did,
            RecusadoEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), "RECUSA", destino.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = destinoId,
            EmissorId = destinoId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "MOVIMENTACAO",
            RegistroOrigemId = movimentacao.Id,
            VestigioId = vestigio.Id,
            Evento = "RECUSA",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Recebimento do vestígio {vestigio.RotuloEvidencia} recusado. Custódia revertida para a origem.";
        return RedirectToAction(nameof(Index));
    }
}
