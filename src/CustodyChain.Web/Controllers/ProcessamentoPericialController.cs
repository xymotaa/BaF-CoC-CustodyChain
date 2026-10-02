using System.Security.Claims;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class ProcessamentoPericialController(
    CustodyChainDbContext db,
    IRomperLacre romperLacre,
    IEmitirLaudo emitirLaudo,
    IFracionarAmostra fracionarAmostra,
    IUnificarAmostras unificarAmostras,
    IRegistrarConsumoOuExaurimento registrarConsumoOuExaurimento) : Controller
{
    [HttpGet("/processamento-pericial")]
    public async Task<IActionResult> Index()
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var agora = DateTime.UtcNow;

        var pericias = await db.Pericias
            .Include(p => p.Vestigio)
            .Include(p => p.Credencial)
            .Where(p => p.PeritoId == peritoId
                && (p.Situacao == SituacaoPericia.DESIGNADA
                    || p.Situacao == SituacaoPericia.RECEBIDA
                    || p.Situacao == SituacaoPericia.EM_EXECUCAO))
            .OrderBy(p => p.SolicitadaEm)
            .ToListAsync();

        var itens = new List<ItemPericiaViewModel>();
        foreach (var p in pericias)
        {
            var lacreAtual = await db.Lacres
                .Where(l => l.VestigioId == p.VestigioId)
                .OrderByDescending(l => l.AplicadoEm)
                .FirstOrDefaultAsync();

            // RN10: só perito com credencial de permissão vigente para
            // aquele vestígio específico pode romper o lacre.
            var credencialValida = p.Credencial is not null
                && p.Credencial.Situacao == SituacaoCredencial.VIGENTE
                && p.Credencial.VestigioId == p.VestigioId
                && (p.Credencial.ValidaAte is null || p.Credencial.ValidaAte > agora);

            itens.Add(new ItemPericiaViewModel(
                p.Id, p.VestigioId, p.Vestigio.RotuloEvidencia, p.Vestigio.RotuloConjunto, p.Vestigio.Descricao,
                p.Situacao.ToString(), lacreAtual?.Numero, credencialValida,
                lacreAtual?.Situacao == SituacaoLacre.ROMPIDO));
        }

        return View(new ProcessamentoPericialListaViewModel { Pericias = itens });
    }

    [HttpPost("/processamento-pericial/receber")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receber(long periciaId)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .FirstOrDefaultAsync(p => p.Id == periciaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.DESIGNADA);

        if (pericia is null)
        {
            TempData["MensagemErro"] = "Perícia não encontrada ou já recebida.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        pericia.Situacao = SituacaoPericia.RECEBIDA;
        pericia.RecebidaEm = agora;
        pericia.Vestigio.CustodianteAtualId = peritoId;
        pericia.Vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Vestígio {pericia.Vestigio.RotuloEvidencia} recebido para perícia.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/romper-lacre")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RomperLacre(RomperLacreViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var resultado = await romperLacre.ExecutarAsync(
                new RomperLacreCommand(peritoId, modelo.PericiaId, modelo.Justificativa), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Lacre {resultado.NumeroLacre} rompido. Vestígio {resultado.RotuloEvidencia} liberado para exame. A ancoragem da credencial está pendente.";
        }
        catch (Exception exception) when (exception is ValidacaoRompimentoLacreException
            or RecursoRompimentoLacreNaoEncontradoException
            or CredencialPermissaoInvalidaException
            or LacreIntactoNaoEncontradoException
            or ConflitoRompimentoLacreException)
        {
            TempData["MensagemErro"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/emitir-laudo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmitirLaudo(EmitirLaudoViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var resultado = await emitirLaudo.ExecutarAsync(
                new EmitirLaudoCommand(peritoId, modelo.PericiaId, modelo.Conteudo), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Laudo {resultado.NumeroLaudo} emitido para o vestígio {resultado.RotuloEvidencia}. A ancoragem da credencial está pendente.";
        }
        catch (Exception exception) when (exception is ValidacaoEmissaoLaudoException
            or RecursoEmissaoLaudoNaoEncontradoException
            or HashVestigioAusenteException
            or ConflitoEmissaoLaudoException)
        {
            TempData["MensagemErro"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/fracionar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fracionar(FracionarViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var resultado = await fracionarAmostra.ExecutarAsync(new FracionarAmostraCommand(
                peritoId,
                modelo.PericiaId,
                modelo.RotuloEvidenciaResultante,
                modelo.DescricaoResultante,
                modelo.QuantidadeDescrita,
                modelo.Justificativa), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Vestígio {resultado.RotuloEvidenciaOrigem} fracionado. Novo item: {resultado.RotuloEvidenciaResultante}. A ancoragem da credencial está pendente.";
        }
        catch (Exception exception) when (exception is ValidacaoFracionamentoAmostraException
            or RecursoFracionamentoAmostraNaoEncontradoException
            or ConflitoFracionamentoAmostraException)
        {
            TempData["MensagemErro"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/unificar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unificar(UnificarViewModel modelo, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId)) return Forbid();
        try
        {
            var resultado = await unificarAmostras.ExecutarAsync(new UnificarAmostrasCommand(peritoId, modelo.PericiaId,
                modelo.OutrosVestigiosOrigemIds, modelo.RotuloEvidenciaResultante, modelo.DescricaoResultante,
                modelo.Justificativa), cancellationToken);
            TempData["MensagemSucesso"] = $"{resultado.QuantidadeOrigens} vestígios unificados em {resultado.RotuloEvidenciaResultante}. A ancoragem da credencial está pendente.";
        }
        catch (Exception exception) when (exception is ValidacaoUnificacaoAmostrasException
            or RecursoUnificacaoAmostrasNaoEncontradoException or ConflitoUnificacaoAmostrasException)
        { TempData["MensagemErro"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/consumir-exaurir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConsumirOuExaurir(
        ConsumirOuExaurirViewModel modelo,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var resultado = await registrarConsumoOuExaurimento.ExecutarAsync(
                new RegistrarConsumoOuExaurimentoCommand(
                    peritoId,
                    modelo.PericiaId,
                    modelo.Tipo,
                    modelo.QuantidadeDescrita,
                    modelo.Justificativa), cancellationToken);
            var nomeOperacao = resultado.Tipo == "CONSUMO" ? "Consumo" : "Exaurimento";
            TempData["MensagemSucesso"] =
                $"{nomeOperacao} registrado para o vestígio {resultado.RotuloEvidencia}. A ancoragem da credencial está pendente.";
        }
        catch (Exception exception) when (exception is ValidacaoConsumoOuExaurimentoException
            or RecursoConsumoOuExaurimentoNaoEncontradoException
            or ConflitoConsumoOuExaurimentoException)
        {
            TempData["MensagemErro"] = exception.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;
}
