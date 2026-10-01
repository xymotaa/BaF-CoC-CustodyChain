using System.Security.Claims;
using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class MovimentacoesController(
    ICriarRemessa criarRemessa,
    IRemessaOpcoesQuery opcoesQuery) : Controller
{
    [HttpGet("/movimentacoes/criar")]
    public async Task<IActionResult> Criar(CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var criadorId))
        {
            return Forbid();
        }

        var modelo = new CriarMovimentacaoViewModel();
        await CarregarOpcoesAsync(modelo, criadorId, cancellationToken);
        return View(modelo);
    }

    [HttpPost("/movimentacoes/criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(CriarMovimentacaoViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var criadorId))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await CarregarOpcoesAsync(modelo, criadorId, cancellationToken);
            return View(modelo);
        }

        try
        {
            var resultado = await criarRemessa.ExecutarAsync(
                new CriarRemessaCommand(
                    criadorId,
                    modelo.VestigioId!.Value,
                    modelo.DestinoId!.Value,
                    modelo.DataHoraSaida!.Value,
                    modelo.CodigoRastreamento),
                cancellationToken);

            TempData["MensagemSucesso"] = $"Remessa do vestígio {resultado.RotuloEvidencia} registrada. A ancoragem da credencial está pendente; aguardando confirmação de {resultado.NomeDestino}.";
            return RedirectToAction(nameof(Criar));
        }
        catch (ValidacaoRemessaException exception)
        {
            AdicionarErro(exception.Campo, exception.Message);
        }
        catch (ConflitoRemessaException exception)
        {
            AdicionarErro(exception.Campo, exception.Message);
        }
        catch (RecursoRemessaNaoEncontradoException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (AtorRemessaNaoAutorizadoException)
        {
            return Forbid();
        }

        await CarregarOpcoesAsync(modelo, criadorId, cancellationToken);
        return View(modelo);
    }

    private async Task CarregarOpcoesAsync(
        CriarMovimentacaoViewModel modelo,
        long criadorId,
        CancellationToken cancellationToken)
    {
        var vestigios = await opcoesQuery.ListarVestigiosDisponiveisAsync(criadorId, cancellationToken);
        var destinos = await opcoesQuery.ListarDestinosDisponiveisAsync(criadorId, cancellationToken);

        modelo.VestigiosDisponiveis = vestigios
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToList();
        modelo.DestinosDisponiveis = destinos
            .Select(d => new OpcaoIntervenienteViewModel(d.Id, d.Nome, d.Perfil))
            .ToList();
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId);

    private void AdicionarErro(string? campo, string mensagem) =>
        ModelState.AddModelError(campo ?? string.Empty, mensagem);
}
