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
public class MovimentacoesController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
{
    [HttpGet("/movimentacoes/criar")]
    public async Task<IActionResult> Criar()
    {
        var modelo = new CriarMovimentacaoViewModel();
        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/movimentacoes/criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(CriarMovimentacaoViewModel modelo)
    {
        var criadorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var vestigio = modelo.VestigioId is not null
            ? await db.Vestigios.FirstOrDefaultAsync(v =>
                v.Id == modelo.VestigioId && v.Estado == EstadoVestigio.Coletado && v.CustodianteAtualId == criadorId)
            : null;

        if (modelo.VestigioId is not null && vestigio is null)
        {
            ModelState.AddModelError(nameof(modelo.VestigioId), "Vestígio não encontrado, não está com você, ou não está mais no estado Coletado.");
        }

        if (modelo.DestinoId == criadorId)
        {
            ModelState.AddModelError(nameof(modelo.DestinoId), "O destino não pode ser você mesmo.");
        }

        if (!ModelState.IsValid || vestigio is null)
        {
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }

        var agora = DateTime.UtcNow;

        var movimentacao = new Movimentacao
        {
            VestigioId = vestigio.Id,
            Tipo = TipoMovimentacao.TRANSPORTE,
            Etapa = 6, // Transporte, art. 158-B
            OrigemId = criadorId,
            DestinoId = modelo.DestinoId!.Value,
            DataHoraSaida = modelo.DataHoraSaida!.Value.ToUniversalTime(),
            CodigoRastreamento = modelo.CodigoRastreamento,
            Situacao = SituacaoMovimentacao.PENDENTE,
            CriadoPorId = criadorId,
        };
        db.Movimentacoes.Add(movimentacao);

        vestigio.Estado = EstadoVestigio.EmTransporte;
        vestigio.EtapaAtual = 6;
        vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        // Credencial #2 (Quadro 14): transferência, no ato do transporte.
        var origem = await db.Intervenientes.FindAsync(criadorId);
        var destino = await db.Intervenientes.FindAsync(modelo.DestinoId!.Value);

        var payload = new
        {
            RE = vestigio.RotuloEvidencia,
            Origem = origem!.Did,
            Destino = destino!.Did,
            DataHoraSaida = movimentacao.DataHoraSaida,
            CodigoRastreamento = movimentacao.CodigoRastreamento,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), "REMESSA", origem.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = criadorId,
            EmissorId = criadorId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "MOVIMENTACAO",
            RegistroOrigemId = movimentacao.Id,
            VestigioId = vestigio.Id,
            Evento = "REMESSA",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Remessa do vestígio {vestigio.RotuloEvidencia} registrada. Aguardando confirmação de {destino.Nome}.";
        return RedirectToAction(nameof(Criar));
    }

    private async Task CarregarOpcoesAsync(CriarMovimentacaoViewModel modelo)
    {
        var criadorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        modelo.VestigiosDisponiveis = await db.Vestigios
            .Where(v => v.Estado == EstadoVestigio.Coletado && v.CustodianteAtualId == criadorId)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync();

        modelo.DestinosDisponiveis = await db.Intervenientes
            .Include(i => i.Perfil)
            .Where(i => i.Situacao == SituacaoInterveniente.ATIVO && i.Id != criadorId)
            .OrderBy(i => i.Perfil.Nome)
            .Select(i => new OpcaoIntervenienteViewModel(i.Id, i.Nome, i.Perfil.Nome))
            .ToListAsync();
    }
}
