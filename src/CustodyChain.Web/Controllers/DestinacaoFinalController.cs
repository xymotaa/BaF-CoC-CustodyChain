using System.Security.Claims;
using CustodyChain.Web.Application.DestinacaoFinal;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class DestinacaoFinalController(
    CustodyChainDbContext db,
    ISolicitarDestinacao solicitarDestinacao,
    IAprovarDestinacao aprovarDestinacao) : Controller
{
    [HttpGet("/destinacao-final")]
    public async Task<IActionResult> Index()
    {
        var pendentes = await db.Descartes
            .Include(d => d.Vestigio)
            .Include(d => d.SolicitadoPor)
            .Where(d => d.AprovadoPorId == null && d.ExecutadoEm == null)
            .OrderBy(d => d.Id)
            .ToListAsync();

        var itens = pendentes.Select(d => new ItemDestinacaoViewModel(
            d.Id, d.VestigioId, d.Vestigio.RotuloEvidencia, d.Tipo.ToString(),
            d.DidMagistrado, d.Observacao, d.SolicitadoPor?.Nome ?? "—", d.AutorizacaoAnexoId))
            .ToList();

        return View(itens);
    }

    [HttpGet("/destinacao-final/solicitar")]
    public async Task<IActionResult> Solicitar()
    {
        var modelo = new SolicitarDestinacaoViewModel();
        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/destinacao-final/solicitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Solicitar(
        SolicitarDestinacaoViewModel modelo,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var solicitanteId))
            return Forbid();

        byte[]? conteudoAutorizacao = null;
        if (modelo.ArquivoAutorizacao is { Length: > 0 })
        {
            await using var memoria = new MemoryStream();
            await modelo.ArquivoAutorizacao.CopyToAsync(memoria, cancellationToken);
            conteudoAutorizacao = memoria.ToArray();
        }

        try
        {
            var resultado = await solicitarDestinacao.ExecutarAsync(new SolicitarDestinacaoCommand(
                solicitanteId,
                modelo.VestigioId,
                modelo.Tipo,
                modelo.DidMagistrado,
                modelo.ArquivoAutorizacao?.FileName,
                conteudoAutorizacao,
                modelo.Observacao), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Destinação final de {resultado.RotuloEvidencia} solicitada. Aguardando aprovação.";
            return RedirectToAction(nameof(Solicitar));
        }
        catch (ValidacaoDestinacaoFinalException exception)
        {
            var campo = exception.Campo == nameof(SolicitarDestinacaoCommand.ConteudoAutorizacao)
                ? nameof(modelo.ArquivoAutorizacao)
                : exception.Campo;
            ModelState.AddModelError(campo ?? string.Empty, exception.Message);
        }
        catch (RecursoDestinacaoFinalNaoEncontradoException exception)
        {
            ModelState.AddModelError(nameof(modelo.VestigioId), exception.Message);
        }
        catch (ConflitoDestinacaoFinalException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/destinacao-final/aprovar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aprovar(long descarteId, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var aprovadorId))
            return Forbid();

        try
        {
            var resultado = await aprovarDestinacao.ExecutarAsync(
                new AprovarDestinacaoCommand(aprovadorId, descarteId), cancellationToken);
            var nomeTipo = resultado.Tipo == "DESCARTE" ? "Descarte" : "Restituição";
            TempData["MensagemSucesso"] =
                $"{nomeTipo} de {resultado.RotuloEvidencia} aprovado e executado. A ancoragem da credencial está pendente.";
        }
        catch (Exception exception) when (exception is ValidacaoDestinacaoFinalException
            or RecursoDestinacaoFinalNaoEncontradoException
            or ConflitoDestinacaoFinalException)
        {
            TempData["MensagemErro"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task CarregarOpcoesAsync(SolicitarDestinacaoViewModel modelo)
    {
        modelo.VestigiosDisponiveis = await db.Vestigios
            .Where(v => v.Estado == EstadoVestigio.Armazenado || v.Estado == EstadoVestigio.Periciado)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync();
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;
}
