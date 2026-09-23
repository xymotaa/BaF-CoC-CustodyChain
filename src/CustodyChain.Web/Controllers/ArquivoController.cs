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
public class ArquivoController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
{
    [HttpGet("/arquivo")]
    public async Task<IActionResult> Index(string? busca, string? categoria)
    {
        var query = db.Vestigios
            .Include(v => v.Processo)
            .Include(v => v.TipoVestigio)
            .Include(v => v.CustodianteAtual)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            query = query.Where(v =>
                v.RotuloEvidencia.Contains(termo) ||
                v.HashSha256 != null && v.HashSha256.Contains(termo) ||
                v.Processo.Numero.Contains(termo) ||
                (v.CustodianteAtual != null && v.CustodianteAtual.Nome.Contains(termo)));
        }

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            query = query.Where(v => v.TipoVestigio.Categoria.ToString() == categoria);
        }

        var vestigios = await query
            .OrderByDescending(v => v.CriadoEm)
            .ToListAsync();

        var vestigioIds = vestigios.Select(v => v.Id).ToList();
        var armazenamentos = await db.Armazenamentos
            .Where(a => vestigioIds.Contains(a.VestigioId) && a.Situacao == SituacaoArmazenamento.GUARDADO)
            .ToDictionaryAsync(a => a.VestigioId);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var itens = vestigios.Select(v =>
        {
            armazenamentos.TryGetValue(v.Id, out var armazenamento);
            return new ItemArquivoViewModel(
                v.Id, v.RotuloEvidencia, v.RotuloConjunto, v.Descricao,
                v.TipoVestigio.Descricao, v.TipoVestigio.Categoria.ToString(),
                v.Processo.Numero, v.CustodianteAtual?.Nome, v.Estado.ToString(),
                v.HashSha256 ?? "", armazenamento?.Central, armazenamento?.Posicao,
                armazenamento?.PrazoGuardaAte,
                armazenamento?.PrazoGuardaAte is not null && armazenamento.PrazoGuardaAte < hoje);
        }).ToList();

        var categorias = await db.TiposVestigio
            .Select(t => t.Categoria.ToString())
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return View(new ArquivoListaViewModel
        {
            Busca = busca,
            CategoriaFiltro = categoria,
            Itens = itens,
            Categorias = categorias,
        });
    }

    [HttpGet("/arquivo/entrada")]
    public async Task<IActionResult> Entrada()
    {
        var modelo = new DarEntradaArquivoViewModel();
        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/arquivo/entrada")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Entrada(DarEntradaArquivoViewModel modelo)
    {
        var recebedorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var vestigio = modelo.VestigioId is not null
            ? await db.Vestigios.FirstOrDefaultAsync(v =>
                v.Id == modelo.VestigioId && v.Estado == EstadoVestigio.Recebido && v.CustodianteAtualId == recebedorId)
            : null;

        if (modelo.VestigioId is not null && vestigio is null)
        {
            ModelState.AddModelError(nameof(modelo.VestigioId), "Vestígio não encontrado, não está com você, ou não está mais no estado Recebido.");
        }

        if (!ModelState.IsValid || vestigio is null)
        {
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }

        var agora = DateTime.UtcNow;

        db.Armazenamentos.Add(new Armazenamento
        {
            VestigioId = vestigio.Id,
            Central = modelo.Central!,
            Posicao = modelo.Posicao,
            EntradaEm = agora,
            PrazoGuardaAte = modelo.PrazoGuardaAte,
            RecebidoPorId = recebedorId,
            Situacao = SituacaoArmazenamento.GUARDADO,
        });

        vestigio.Estado = EstadoVestigio.Armazenado;
        vestigio.EtapaAtual = 9; // Armazenamento, art. 158-B
        vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        var recebedor = await db.Intervenientes.FindAsync(recebedorId);
        var payload = new
        {
            RE = vestigio.RotuloEvidencia,
            Central = modelo.Central,
            Posicao = modelo.Posicao,
            PrazoGuardaAte = modelo.PrazoGuardaAte,
            Responsavel = recebedor!.Did,
            EntradaEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        // Credencial #6 (Quadro 14): armazenamento.
        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), "GUARDA", recebedor.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = recebedorId,
            EmissorId = recebedorId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "VESTIGIO",
            RegistroOrigemId = vestigio.Id,
            VestigioId = vestigio.Id,
            Evento = "GUARDA",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Vestígio {vestigio.RotuloEvidencia} arquivado.";
        return RedirectToAction(nameof(Entrada));
    }

    private async Task CarregarOpcoesAsync(DarEntradaArquivoViewModel modelo)
    {
        var recebedorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        modelo.VestigiosDisponiveis = await db.Vestigios
            .Where(v => v.Estado == EstadoVestigio.Recebido && v.CustodianteAtualId == recebedorId)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync();
    }
}
