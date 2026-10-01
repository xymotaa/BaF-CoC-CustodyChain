using System.Security.Claims;
using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustodyChain.Web.Controllers;

[Authorize(Policy = PoliticasAutorizacao.CadastrarVestigio)]
public class VestigiosController(
    ICadastrarVestigio cadastrarVestigio,
    ICadastroVestigioOpcoesQuery opcoesQuery) : Controller
{
    [HttpGet("/vestigios/cadastrar")]
    public async Task<IActionResult> Cadastrar(CancellationToken cancellationToken)
    {
        var modelo = new CadastroVestigioViewModel();
        await CarregarOpcoesAsync(modelo, cancellationToken);
        return View(modelo);
    }

    [HttpPost("/vestigios/cadastrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cadastrar(CadastroVestigioViewModel modelo, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await CarregarOpcoesAsync(modelo, cancellationToken);
            return View(modelo);
        }

        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var criadorId))
        {
            return Forbid();
        }

        try
        {
            var resultado = await cadastrarVestigio.ExecutarAsync(
                new CadastrarVestigioCommand(
                    criadorId,
                    modelo.RotuloEvidencia!,
                    modelo.RotuloConjunto!,
                    modelo.NumeroEvidencia,
                    modelo.ProcessoId!.Value,
                    modelo.TipoVestigioId!.Value,
                    modelo.Descricao!,
                    modelo.LocalColeta,
                    modelo.DataHoraColeta!.Value,
                    modelo.MetodoColeta,
                    modelo.NumeroLacre!,
                    modelo.HouveIntercorrencia,
                    modelo.DescricaoIntercorrencia),
                cancellationToken);

            TempData["MensagemSucesso"] = $"Vestígio {resultado.RotuloEvidencia} cadastrado. A ancoragem da credencial está pendente.";
            return RedirectToAction(nameof(Cadastrar));
        }
        catch (ValidacaoCadastroVestigioException exception)
        {
            AdicionarErro(exception.Campo, exception.Message);
        }
        catch (ConflitoCadastroVestigioException exception)
        {
            AdicionarErro(exception.Campo, exception.Message);
        }
        catch (RecursoCadastroVestigioNaoEncontradoException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (AtorCadastroVestigioNaoAutorizadoException)
        {
            return Forbid();
        }

        await CarregarOpcoesAsync(modelo, cancellationToken);
        return View(modelo);
    }

    private async Task CarregarOpcoesAsync(CadastroVestigioViewModel modelo, CancellationToken cancellationToken)
    {
        var processos = await opcoesQuery.ListarProcessosAtivosAsync(cancellationToken);
        var tipos = await opcoesQuery.ListarTiposAsync(cancellationToken);

        modelo.ProcessosDisponiveis = processos
            .Select(p => new OpcaoProcessoViewModel(p.Id, p.Numero, p.NomeOperacao))
            .ToList();
        modelo.TiposDisponiveis = tipos
            .Select(t => new OpcaoTipoVestigioViewModel(t.Id, t.Descricao, t.Categoria))
            .ToList();
    }

    private void AdicionarErro(string? campo, string mensagem) =>
        ModelState.AddModelError(campo ?? string.Empty, mensagem);
}
