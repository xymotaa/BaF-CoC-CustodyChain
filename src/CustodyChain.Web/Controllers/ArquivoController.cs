using System.Security.Claims;
using CustodyChain.Web.Application.Arquivo;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class ArquivoController(CustodyChainDbContext db, IDarEntradaArquivo darEntradaArquivo) : Controller
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
    public async Task<IActionResult> Entrada(DarEntradaArquivoViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var recebedorId))
            return Forbid();

        if (!ModelState.IsValid)
        {
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }

        try
        {
            var resultado = await darEntradaArquivo.ExecutarAsync(new DarEntradaArquivoCommand(
                recebedorId,
                modelo.VestigioId ?? 0,
                modelo.Central,
                modelo.Posicao,
                modelo.PrazoGuardaAte), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Vestígio {resultado.RotuloEvidencia} arquivado. A ancoragem da credencial está pendente.";
        }
        catch (ValidacaoEntradaArquivoException exception)
        {
            ModelState.AddModelError(exception.Campo ?? string.Empty, exception.Message);
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }
        catch (RecursoEntradaArquivoNaoEncontradoException exception)
        {
            ModelState.AddModelError(nameof(modelo.VestigioId), exception.Message);
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }
        catch (ConflitoEntradaArquivoException exception)
        {
            TempData["MensagemErro"] = exception.Message;
        }

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

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;
}
