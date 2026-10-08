using System.Security.Claims;
using System.Text.Json;
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
    IAprovarDestinacao aprovarDestinacao,
    IConfiguration configuration) : Controller
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

    [HttpPost("/destinacao-final/solicitar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararSolicitacao(
        SolicitarDestinacaoViewModel modelo,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var solicitanteId))
            return Forbid();

        try
        {
            var preparacao = await solicitarDestinacao.PrepararAsync(new PrepararSolicitacaoDestinacaoCommand(
                solicitanteId,
                modelo.VestigioId,
                modelo.Tipo,
                modelo.DidMagistrado,
                modelo.ArquivoAutorizacao?.FileName,
                await LerConteudoAsync(modelo.ArquivoAutorizacao, cancellationToken),
                modelo.Observacao), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidSignatario,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (AtorDestinacaoFinalNaoAutorizadoException)
        {
            return Forbid();
        }
        catch (Exception exception) when (exception is ValidacaoDestinacaoFinalException
            or RecursoDestinacaoFinalNaoEncontradoException or ConflitoDestinacaoFinalException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/destinacao-final/solicitar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirSolicitacao(
        [FromBody] SolicitacaoDestinacaoComProvaRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null) return BadRequest(new { message = "Informe a prova da solicitação." });
        if (!TryObterIntervenienteId(out var solicitanteId)) return Forbid();

        try
        {
            var resultado = await solicitarDestinacao.ExecutarAsync(new ConcluirSolicitacaoDestinacaoCommand(
                solicitanteId, requisicao.VestigioId, requisicao.Operation), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Destinação final de {resultado.RotuloEvidencia} solicitada e confirmada no ledger. Aguardando aprovação.";
            return Ok(new { redirectUrl = Url.Action(nameof(Solicitar)) });
        }
        catch (AtorDestinacaoFinalNaoAutorizadoException) { return Forbid(); }
        catch (IndisponibilidadeLedgerDestinacaoFinalException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (Exception exception) when (exception is ValidacaoDestinacaoFinalException
            or RecursoDestinacaoFinalNaoEncontradoException or ConflitoDestinacaoFinalException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/destinacao-final/aprovar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararAprovacao(
        [FromBody] PrepararAprovacaoDestinacaoRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null) return BadRequest(new { message = "Informe a destinação a aprovar." });
        if (!TryObterIntervenienteId(out var aprovadorId)) return Forbid();

        try
        {
            var preparacao = await aprovarDestinacao.PrepararAsync(
                new PrepararAprovacaoDestinacaoCommand(aprovadorId, requisicao.DescarteId), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidSignatario,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (AtorDestinacaoFinalNaoAutorizadoException) { return Forbid(); }
        catch (Exception exception) when (exception is ValidacaoDestinacaoFinalException
            or RecursoDestinacaoFinalNaoEncontradoException or ConflitoDestinacaoFinalException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/destinacao-final/aprovar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirAprovacao(
        [FromBody] AprovacaoDestinacaoComProvaRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null) return BadRequest(new { message = "Informe a prova da aprovação." });
        if (!TryObterIntervenienteId(out var aprovadorId)) return Forbid();

        try
        {
            var resultado = await aprovarDestinacao.ExecutarAsync(new ConcluirAprovacaoDestinacaoCommand(
                aprovadorId, requisicao.DescarteId, requisicao.Operation), cancellationToken);
            var nomeTipo = resultado.Tipo == "DESCARTE" ? "Descarte" : "Restituição";
            TempData["MensagemSucesso"] =
                $"{nomeTipo} de {resultado.RotuloEvidencia} aprovado e executado com confirmação no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (AtorDestinacaoFinalNaoAutorizadoException) { return Forbid(); }
        catch (IndisponibilidadeLedgerDestinacaoFinalException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (Exception exception) when (exception is ValidacaoDestinacaoFinalException
            or RecursoDestinacaoFinalNaoEncontradoException
            or ConflitoDestinacaoFinalException)
        {
            return BadRequest(new { message = exception.Message });
        }
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

    private static async Task<byte[]?> LerConteudoAsync(IFormFile? arquivo, CancellationToken cancellationToken)
    {
        if (arquivo is not { Length: > 0 }) return null;
        await using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, cancellationToken);
        return memoria.ToArray();
    }
}

public sealed record SolicitacaoDestinacaoComProvaRequest(long VestigioId, JsonElement Operation);
public sealed record PrepararAprovacaoDestinacaoRequest(long DescarteId);
public sealed record AprovacaoDestinacaoComProvaRequest(long DescarteId, JsonElement Operation);
