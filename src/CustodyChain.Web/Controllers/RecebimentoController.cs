using System.Security.Claims;
using System.Text.Json;
using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class RecebimentoController(
    IConfirmarRecebimento confirmarRecebimento,
    IRecusarRecebimento recusarRecebimento,
    IRecebimentoPendentesQuery recebimentoPendentesQuery,
    IConfiguration configuration) : Controller
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

    [HttpPost("/recebimento/confirmar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararConfirmacao(
        [FromBody] ConfirmarRecebimentoViewModel? modelo,
        CancellationToken cancellationToken)
    {
        if (modelo is null) return BadRequest(new { message = "Informe os dados do recebimento." });
        if (!TryObterIntervenienteId(out var destinoId))
            return Forbid();

        try
        {
            var preparacao = await confirmarRecebimento.PrepararAsync(
                new ConfirmarRecebimentoCommand(destinoId, modelo.MovimentacaoId, modelo.NumeroLacreConferido), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidSignatario,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (AtorRecebimentoNaoAutorizadoException) { return Forbid(); }
        catch (Exception exception) when (exception is ValidacaoRecebimentoException
            or RecursoRecebimentoNaoEncontradoException or ConflitoRecebimentoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/recebimento/confirmar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirConfirmacao(
        [FromBody] ConfirmarRecebimentoComProvaRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null) return BadRequest(new { message = "Informe a prova do recebimento." });
        if (!TryObterIntervenienteId(out var destinoId))
            return Forbid();

        try
        {
            var resultado = await confirmarRecebimento.ExecutarAsync(new ConcluirConfirmarRecebimentoCommand(
                new ConfirmarRecebimentoCommand(destinoId, requisicao.Recebimento.MovimentacaoId, requisicao.Recebimento.NumeroLacreConferido),
                requisicao.Operation), cancellationToken);
            TempData["MensagemSucesso"] = resultado.LacreConfere
                ? $"Vestígio {resultado.RotuloEvidencia} recebido e confirmado no ledger."
                : $"Divergência no lacre do vestígio {resultado.RotuloEvidencia}; custódia marcada como comprometida.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (AtorRecebimentoNaoAutorizadoException) { return Forbid(); }
        catch (IndisponibilidadeLedgerRecebimentoException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (Exception exception) when (exception is ValidacaoRecebimentoException
            or RecursoRecebimentoNaoEncontradoException or ConflitoRecebimentoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/recebimento/recusar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararRecusa(
        [FromBody] RecusarRecebimentoViewModel? modelo,
        CancellationToken cancellationToken)
    {
        if (modelo is null) return BadRequest(new { message = "Informe os dados da recusa." });
        if (!TryObterIntervenienteId(out var destinoId)) return Forbid();

        try
        {
            var preparacao = await recusarRecebimento.PrepararAsync(
                new RecusarRecebimentoCommand(destinoId, modelo.MovimentacaoId, modelo.MotivoRecusa), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidSignatario,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (AtorRecebimentoNaoAutorizadoException) { return Forbid(); }
        catch (Exception exception) when (exception is ValidacaoRecebimentoException
            or RecursoRecebimentoNaoEncontradoException or ConflitoRecebimentoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/recebimento/recusar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirRecusa(
        [FromBody] RecusarRecebimentoComProvaRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null) return BadRequest(new { message = "Informe a prova da recusa." });
        if (!TryObterIntervenienteId(out var destinoId)) return Forbid();

        try
        {
            var resultado = await recusarRecebimento.ExecutarAsync(new ConcluirRecusarRecebimentoCommand(
                new RecusarRecebimentoCommand(destinoId, requisicao.Recusa.MovimentacaoId, requisicao.Recusa.MotivoRecusa),
                requisicao.Operation), cancellationToken);
            TempData["MensagemSucesso"] = $"Recebimento do vestígio {resultado.RotuloEvidencia} recusado e confirmado no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (AtorRecebimentoNaoAutorizadoException) { return Forbid(); }
        catch (IndisponibilidadeLedgerRecebimentoException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (Exception exception) when (exception is ValidacaoRecebimentoException
            or RecursoRecebimentoNaoEncontradoException or ConflitoRecebimentoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;
}

public sealed record ConfirmarRecebimentoComProvaRequest(ConfirmarRecebimentoViewModel Recebimento, JsonElement Operation);
public sealed record RecusarRecebimentoComProvaRequest(RecusarRecebimentoViewModel Recusa, JsonElement Operation);
