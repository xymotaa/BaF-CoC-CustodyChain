using System.Security.Claims;
using CustodyChain.Web.Application.Pericias;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class PericiasController(
    CustodyChainDbContext db,
    ISolicitarDesignacaoPericia solicitarDesignacao,
    IAprovarDesignacaoPericia aprovarDesignacao,
    IConfiguration configuration,
    ILogger<PericiasController> logger) : Controller
{
    [Authorize(Roles = "CUSTODIA")]
    [HttpGet("/pericias/designar")]
    public async Task<IActionResult> Designar()
    {
        var modelo = new DesignarPericiaViewModel();
        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [Authorize(Roles = "CUSTODIA")]
    [HttpPost("/pericias/designar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Designar(
        DesignarPericiaViewModel modelo,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var solicitanteId))
            return Forbid();

        try
        {
            var resultado = await solicitarDesignacao.ExecutarAsync(
                new SolicitarDesignacaoPericiaCommand(
                    solicitanteId,
                    modelo.VestigioId,
                    modelo.PeritoId,
                    modelo.AreaPericial,
                    modelo.Prioridade),
                cancellationToken);
            TempData["MensagemSucesso"] =
                $"Perícia do vestígio {resultado.RotuloEvidencia} solicitada para {resultado.PeritoNome}. Aguardando aprovação administrativa.";
            return RedirectToAction(nameof(Designar));
        }
        catch (ValidacaoDesignacaoPericiaException exception)
        {
            ModelState.AddModelError(exception.Campo ?? string.Empty, exception.Message);
        }
        catch (RecursoDesignacaoPericiaNaoEncontradoException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (ConflitoDesignacaoPericiaException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet("/pericias/solicitacoes")]
    public async Task<IActionResult> Solicitacoes(CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var aprovadorId))
            return Forbid();

        var solicitacoes = await aprovarDesignacao.ListarPendentesAsync(aprovadorId, cancellationToken);
        return View(new SolicitacoesDesignacaoPericiaViewModel
        {
            Solicitacoes = solicitacoes.Select(item => new SolicitacaoDesignacaoPericiaItemViewModel(
                item.PericiaId,
                item.RotuloEvidencia,
                item.PeritoNome,
                item.AreaPericial,
                item.Prioridade,
                item.SolicitadaEm)).ToList()
        });
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet("/pericias/{periciaId:long}/aprovar")]
    public async Task<IActionResult> Aprovar(long periciaId, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var aprovadorId))
            return Forbid();

        try
        {
            var detalhe = await aprovarDesignacao.ObterDetalheAsync(periciaId, aprovadorId, cancellationToken);
            return View(new AprovarDesignacaoPericiaViewModel(
                detalhe.PericiaId,
                detalhe.RotuloEvidencia,
                detalhe.DescricaoVestigio,
                detalhe.PeritoNome,
                detalhe.DidPerito,
                detalhe.AreaPericial,
                detalhe.Prioridade,
                detalhe.SolicitadaEm));
        }
        catch (RecursoDesignacaoPericiaNaoEncontradoException exception)
        {
            TempData["MensagemErro"] = exception.Message;
            return RedirectToAction(nameof(Solicitacoes));
        }
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("/pericias/{periciaId:long}/aprovar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararAprovacao(
        long periciaId,
        [FromBody] PrepararAprovacaoDesignacaoPericiaRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var aprovadorId))
            return Forbid();

        try
        {
            var preparacao = await aprovarDesignacao.PrepararAsync(
                new PrepararAprovacaoDesignacaoPericiaCommand(
                    periciaId,
                    aprovadorId,
                    request?.ValidaAte),
                cancellationToken);
            return Ok(new
            {
                credential = preparacao.Credencial,
                issuerDid = preparacao.DidEmissor,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (Exception exception) when (exception is ValidacaoDesignacaoPericiaException
            or RecursoDesignacaoPericiaNaoEncontradoException
            or ConflitoDesignacaoPericiaException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("/pericias/{periciaId:long}/aprovar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirAprovacao(
        long periciaId,
        [FromBody] EnviarProvaAprovacaoDesignacaoPericiaRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var aprovadorId))
            return Forbid();
        if (request is null)
            return BadRequest(new { message = "Informe a VC assinada pela wallet administrativa." });

        try
        {
            var resultado = await aprovarDesignacao.ConcluirAsync(
                new ConcluirAprovacaoDesignacaoPericiaCommand(
                    periciaId,
                    aprovadorId,
                    request.Credential),
                cancellationToken);
            TempData["MensagemSucesso"] =
                $"Perícia do vestígio {resultado.RotuloEvidencia} designada a {resultado.PeritoNome}.";
            return Ok(new { redirectUrl = Url.Action(nameof(Solicitacoes)) });
        }
        catch (IndisponibilidadeLedgerDesignacaoPericiaException exception)
        {
            logger.LogError(exception.InnerException, "Falha ao registrar a VC da perícia {PericiaId} no ledger.", periciaId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (Exception exception) when (exception is ValidacaoDesignacaoPericiaException
            or RecursoDesignacaoPericiaNaoEncontradoException
            or ConflitoDesignacaoPericiaException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task CarregarOpcoesAsync(DesignarPericiaViewModel modelo)
    {
        var situacoesAtivas = new[]
        {
            SituacaoPericia.SOLICITADA,
            SituacaoPericia.DESIGNADA,
            SituacaoPericia.RECEBIDA,
            SituacaoPericia.EM_EXECUCAO
        };
        modelo.VestigiosDisponiveis = await db.Vestigios
            .Where(v => v.Estado == EstadoVestigio.Armazenado
                && !db.Pericias.Any(p => p.VestigioId == v.Id && situacoesAtivas.Contains(p.Situacao)))
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync();

        modelo.PeritosDisponiveis = await db.Intervenientes.AptosParaOperacoesLedger()
            .Include(i => i.Perfil)
            .Where(i => i.Situacao == SituacaoInterveniente.ATIVO && i.Perfil.Codigo == "PERITO")
            .OrderBy(i => i.Nome)
            .Select(i => new OpcaoIntervenienteViewModel(i.Id, i.Nome, i.Perfil.Nome))
            .ToListAsync();
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId)
        && intervenienteId > 0;
}
