using System.Security.Claims;
using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class RecebimentoController(
    IConfirmarRecebimento confirmarRecebimento,
    IRecusarRecebimento recusarRecebimento,
    IRecebimentoPendentesQuery recebimentoPendentesQuery) : Controller
{
    [HttpGet("/recebimento")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var destinoId))
            return Forbid();

        var pendentes = await recebimentoPendentesQuery.ListarAsync(destinoId, cancellationToken);
        return View(new RecebimentoListaViewModel
        {
            Pendentes = pendentes.Select(item => new ItemRecebimentoViewModel(
                item.MovimentacaoId, item.VestigioId, item.RotuloEvidencia, item.Descricao,
                item.OrigemNome, item.DataHoraSaida, item.CodigoRastreamento,
                item.NumeroLacreEsperado)).ToList(),
        });
    }

    [HttpPost("/recebimento/confirmar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmar(ConfirmarRecebimentoViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var destinoId))
            return Forbid();

        try
        {
            var resultado = await confirmarRecebimento.ExecutarAsync(
                new ConfirmarRecebimentoCommand(destinoId, modelo.MovimentacaoId, modelo.NumeroLacreConferido), cancellationToken);
            TempData["MensagemSucesso"] = resultado.LacreConfere
                ? $"Vestígio {resultado.RotuloEvidencia} recebido. Lacre conferido."
                : $"Divergência no lacre do vestígio {resultado.RotuloEvidencia}. Custódia marcada como comprometida.";
        }
        catch (Exception exception) when (exception is ValidacaoRecebimentoException
            or RecursoRecebimentoNaoEncontradoException or ConflitoRecebimentoException)
        {
            TempData["MensagemErro"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/recebimento/recusar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Recusar(RecusarRecebimentoViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var destinoId))
            return Forbid();

        try
        {
            var resultado = await recusarRecebimento.ExecutarAsync(
                new RecusarRecebimentoCommand(destinoId, modelo.MovimentacaoId, modelo.MotivoRecusa), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Recebimento do vestígio {resultado.RotuloEvidencia} recusado. Custódia revertida para a origem.";
        }
        catch (Exception exception) when (exception is ValidacaoRecebimentoException
            or RecursoRecebimentoNaoEncontradoException or ConflitoRecebimentoException)
        {
            TempData["MensagemErro"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;
}
