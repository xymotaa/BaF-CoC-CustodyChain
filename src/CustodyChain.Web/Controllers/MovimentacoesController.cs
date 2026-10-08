using System.Security.Claims;
using System.Text.Json;
using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class MovimentacoesController(
    ICriarRemessa criarRemessa,
    IRemessaOpcoesQuery opcoesQuery,
    IConfiguration configuration) : Controller
{
    [HttpGet("/movimentacoes/criar")]
    public async Task<IActionResult> Criar(CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var criadorId)) return Forbid();

        var modelo = new CriarMovimentacaoViewModel();
        await CarregarOpcoesAsync(modelo, criadorId, cancellationToken);
        return View(modelo);
    }

    [HttpPost("/movimentacoes/criar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararRemessa(
        [FromBody] CriarMovimentacaoViewModel? modelo,
        CancellationToken cancellationToken)
    {
        if (modelo is null) return BadRequest(new { message = "Informe os dados da remessa." });
        if (!TryObterIntervenienteId(out var criadorId)) return Forbid();

        try
        {
            var preparacao = await criarRemessa.PrepararAsync(CriarCommand(criadorId, modelo), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidSignatario,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (AtorRemessaNaoAutorizadoException) { return Forbid(); }
        catch (Exception exception) when (exception is ValidacaoRemessaException
                                          or ConflitoRemessaException
                                          or RecursoRemessaNaoEncontradoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/movimentacoes/criar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirRemessa(
        [FromBody] CriarRemessaComProvaRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null) return BadRequest(new { message = "Informe a prova da remessa." });
        if (!TryObterIntervenienteId(out var criadorId)) return Forbid();

        try
        {
            var resultado = await criarRemessa.ExecutarAsync(
                new ConcluirRemessaCommand(CriarCommand(criadorId, requisicao.Remessa), requisicao.Operation),
                cancellationToken);

            TempData["MensagemSucesso"] =
                $"Remessa do vestígio {resultado.RotuloEvidencia} confirmada no ledger; aguardando {resultado.NomeDestino}.";
            return Ok(new { redirectUrl = Url.Action(nameof(Criar)) });
        }
        catch (AtorRemessaNaoAutorizadoException) { return Forbid(); }
        catch (IndisponibilidadeLedgerRemessaException exception)
        {
            return StatusCode(503, new { message = exception.Message });
        }
        catch (Exception exception) when (exception is ValidacaoRemessaException
                                          or ConflitoRemessaException
                                          or RecursoRemessaNaoEncontradoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private static CriarRemessaCommand CriarCommand(long criadorId, CriarMovimentacaoViewModel modelo) =>
        new(
            criadorId,
            modelo.VestigioId ?? 0,
            modelo.DestinoId ?? 0,
            modelo.DataHoraSaida ?? default,
            modelo.CodigoRastreamento);

    private async Task CarregarOpcoesAsync(
        CriarMovimentacaoViewModel modelo,
        long criadorId,
        CancellationToken cancellationToken)
    {
        var vestigios = await opcoesQuery.ListarVestigiosDisponiveisAsync(criadorId, cancellationToken);
        var destinos = await opcoesQuery.ListarDestinosDisponiveisAsync(criadorId, cancellationToken);
        modelo.VestigiosDisponiveis = vestigios
            .Select(vestigio => new OpcaoVestigioViewModel(vestigio.Id, vestigio.RotuloEvidencia, vestigio.Descricao)).ToList();
        modelo.DestinosDisponiveis = destinos
            .Select(destino => new OpcaoIntervenienteViewModel(destino.Id, destino.Nome, destino.Perfil)).ToList();
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId);
}

public sealed record CriarRemessaComProvaRequest(
    CriarMovimentacaoViewModel Remessa,
    JsonElement Operation);
