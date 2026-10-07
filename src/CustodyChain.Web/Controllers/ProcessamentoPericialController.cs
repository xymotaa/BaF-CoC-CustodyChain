using System.Security.Claims;
using System.Text.Json;
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
    IReceberPericia receberPericia,
    IRomperLacre romperLacre,
    IEmitirLaudo emitirLaudo,
    IFracionarAmostra fracionarAmostra,
    IUnificarAmostras unificarAmostras,
    IRegistrarConsumoOuExaurimento registrarConsumoOuExaurimento,
    IConfiguration configuration) : Controller
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

    [HttpPost("/processamento-pericial/{periciaId:long}/receber/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararRecebimento(long periciaId, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var preparacao = await receberPericia.PrepararAsync(
                new ReceberPericiaCommand(peritoId, periciaId), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidPerito,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (Exception exception) when (exception is ValidacaoRecebimentoPericiaException
            or RecursoRecebimentoPericiaNaoEncontradoException
            or ConflitoRecebimentoPericiaException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/receber/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirRecebimento(
        long periciaId,
        [FromBody] EnviarOperacaoPericialRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();
        if (request is null)
            return BadRequest(new { message = "Informe a operação de recebimento assinada pela wallet." });

        try
        {
            var resultado = await receberPericia.ExecutarAsync(
                new ConcluirRecebimentoPericiaCommand(peritoId, periciaId, request.Operation), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Vestígio {resultado.RotuloEvidencia} recebido para perícia. A autorização foi confirmada no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (IndisponibilidadeLedgerRecebimentoPericiaException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (Exception exception) when (exception is ValidacaoRecebimentoPericiaException
            or RecursoRecebimentoPericiaNaoEncontradoException
            or ConflitoRecebimentoPericiaException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/romper-lacre/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararRompimentoLacre(
        long periciaId,
        [FromBody] PrepararRompimentoLacreRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var preparacao = await romperLacre.PrepararAsync(
                new RomperLacreCommand(peritoId, periciaId, request?.Justificativa), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidPerito,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (Exception exception) when (exception is ValidacaoRompimentoLacreException
            or RecursoRompimentoLacreNaoEncontradoException
            or CredencialPermissaoInvalidaException
            or LacreIntactoNaoEncontradoException
            or ConflitoRompimentoLacreException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/romper-lacre/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirRompimentoLacre(
        long periciaId,
        [FromBody] EnviarOperacaoRompimentoLacreRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();
        if (request is null)
            return BadRequest(new { message = "Informe a operação de rompimento assinada pela wallet." });

        try
        {
            var resultado = await romperLacre.ExecutarAsync(new ConcluirRompimentoLacreCommand(
                peritoId, periciaId, request.Justificativa, request.Operation), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Lacre {resultado.NumeroLacre} rompido. Vestígio {resultado.RotuloEvidencia} liberado para exame. A autorização foi confirmada no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (IndisponibilidadeLedgerRompimentoLacreException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (Exception exception) when (exception is ValidacaoRompimentoLacreException
            or RecursoRompimentoLacreNaoEncontradoException
            or CredencialPermissaoInvalidaException
            or LacreIntactoNaoEncontradoException
            or ConflitoRompimentoLacreException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/emitir-laudo/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararEmissaoLaudo(
        long periciaId,
        [FromBody] PrepararEmissaoLaudoRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var preparacao = await emitirLaudo.PrepararAsync(
                new EmitirLaudoCommand(peritoId, periciaId, request?.Conteudo), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidPerito,
                numeroLaudo = preparacao.NumeroLaudo,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (Exception exception) when (exception is ValidacaoEmissaoLaudoException
            or RecursoEmissaoLaudoNaoEncontradoException
            or HashVestigioAusenteException
            or ConflitoEmissaoLaudoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/emitir-laudo/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirEmissaoLaudo(
        long periciaId,
        [FromBody] EnviarOperacaoLaudoRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();
        if (request is null)
            return BadRequest(new { message = "Informe a operação de laudo assinada pela wallet." });

        try
        {
            var resultado = await emitirLaudo.ExecutarAsync(
                new ConcluirEmissaoLaudoCommand(peritoId, periciaId, request.Conteudo, request.Operation), cancellationToken);
            TempData["MensagemSucesso"] =
                $"Laudo {resultado.NumeroLaudo} emitido para o vestígio {resultado.RotuloEvidencia}. A autorização foi confirmada no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (IndisponibilidadeLedgerEmissaoLaudoException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (Exception exception) when (exception is ValidacaoEmissaoLaudoException
            or RecursoEmissaoLaudoNaoEncontradoException
            or HashVestigioAusenteException
            or ConflitoEmissaoLaudoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/fracionar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararFracionamento(long periciaId, [FromBody] PrepararFracionamentoRequest? request, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var preparacao = await fracionarAmostra.PrepararAsync(new FracionarAmostraCommand(
                peritoId,
                periciaId, request?.RotuloEvidenciaResultante, request?.DescricaoResultante, request?.QuantidadeDescrita, request?.Justificativa), cancellationToken);
            return Ok(new { operation = preparacao.Operacao, signerDid = preparacao.DidPerito, walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123" });
        }
        catch (Exception exception) when (exception is ValidacaoFracionamentoAmostraException
            or RecursoFracionamentoAmostraNaoEncontradoException
            or ConflitoFracionamentoAmostraException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/fracionar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirFracionamento(long periciaId, [FromBody] EnviarOperacaoFracionamentoRequest? request, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId)) return Forbid();
        if (request is null) return BadRequest(new { message = "Informe a operação assinada pela wallet." });
        try
        {
            var resultado = await fracionarAmostra.ExecutarAsync(new ConcluirFracionamentoAmostraCommand(peritoId, periciaId, request.RotuloEvidenciaResultante, request.DescricaoResultante, request.QuantidadeDescrita, request.Justificativa, request.Operation), cancellationToken);
            TempData["MensagemSucesso"] = $"Vestígio {resultado.RotuloEvidenciaOrigem} fracionado. Novo item: {resultado.RotuloEvidenciaResultante}. A autorização foi confirmada no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (IndisponibilidadeLedgerFracionamentoAmostraException x) { return StatusCode(503, new { message = x.Message }); }
        catch (Exception x) when (x is ValidacaoFracionamentoAmostraException or RecursoFracionamentoAmostraNaoEncontradoException or ConflitoFracionamentoAmostraException) { return BadRequest(new { message = x.Message }); }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/unificar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararUnificacao(long periciaId, [FromBody] PrepararUnificacaoRequest? request, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId)) return Forbid();
        try
        {
            var preparacao = await unificarAmostras.PrepararAsync(new UnificarAmostrasCommand(peritoId, periciaId,
                request?.OutrosVestigiosOrigemIds, request?.RotuloEvidenciaResultante, request?.DescricaoResultante,
                request?.Justificativa), cancellationToken);
            return Ok(new { operation = preparacao.Operacao, signerDid = preparacao.DidPerito, quantidadeOrigens = preparacao.QuantidadeOrigens, walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123" });
        }
        catch (Exception exception) when (exception is ValidacaoUnificacaoAmostrasException
            or RecursoUnificacaoAmostrasNaoEncontradoException or ConflitoUnificacaoAmostrasException)
        { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/unificar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirUnificacao(long periciaId, [FromBody] EnviarOperacaoUnificacaoRequest? request, CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId)) return Forbid();
        if (request is null) return BadRequest(new { message = "Informe a operação assinada pela wallet." });
        try
        {
            var resultado = await unificarAmostras.ExecutarAsync(new ConcluirUnificacaoAmostrasCommand(peritoId, periciaId,
                request.OutrosVestigiosOrigemIds, request.RotuloEvidenciaResultante, request.DescricaoResultante,
                request.Justificativa, request.Operation), cancellationToken);
            TempData["MensagemSucesso"] = $"{resultado.QuantidadeOrigens} vestígios unificados em {resultado.RotuloEvidenciaResultante}. A autorização foi confirmada no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (IndisponibilidadeLedgerUnificacaoAmostrasException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (Exception exception) when (exception is ValidacaoUnificacaoAmostrasException or RecursoUnificacaoAmostrasNaoEncontradoException or ConflitoUnificacaoAmostrasException) { return BadRequest(new { message = exception.Message }); }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/consumir-exaurir/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararConsumoOuExaurimento(
        long periciaId,
        [FromBody] PrepararConsumoOuExaurimentoRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();

        try
        {
            var preparacao = await registrarConsumoOuExaurimento.PrepararAsync(
                new RegistrarConsumoOuExaurimentoCommand(
                    peritoId,
                    periciaId,
                    request?.Tipo,
                    request?.QuantidadeDescrita,
                    request?.Justificativa), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidPerito,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (Exception exception) when (exception is ValidacaoConsumoOuExaurimentoException
            or RecursoConsumoOuExaurimentoNaoEncontradoException
            or ConflitoConsumoOuExaurimentoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/processamento-pericial/{periciaId:long}/consumir-exaurir/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirConsumoOuExaurimento(
        long periciaId,
        [FromBody] EnviarOperacaoConsumoOuExaurimentoRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryObterIntervenienteId(out var peritoId))
            return Forbid();
        if (request is null)
            return BadRequest(new { message = "Informe a operação assinada pela wallet." });

        try
        {
            var resultado = await registrarConsumoOuExaurimento.ExecutarAsync(
                new ConcluirConsumoOuExaurimentoCommand(
                    peritoId,
                    periciaId,
                    request.Tipo,
                    request.QuantidadeDescrita,
                    request.Justificativa,
                    request.Operation), cancellationToken);
            var nomeOperacao = resultado.Tipo == "CONSUMO" ? "Consumo" : "Exaurimento";
            TempData["MensagemSucesso"] =
                $"{nomeOperacao} registrado para o vestígio {resultado.RotuloEvidencia}. A autorização foi confirmada no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Index)) });
        }
        catch (IndisponibilidadeLedgerConsumoOuExaurimentoException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = exception.Message });
        }
        catch (Exception exception) when (exception is ValidacaoConsumoOuExaurimentoException
            or RecursoConsumoOuExaurimentoNaoEncontradoException
            or ConflitoConsumoOuExaurimentoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;

    public sealed record PrepararEmissaoLaudoRequest(string? Conteudo);

    public sealed record EnviarOperacaoLaudoRequest(string? Conteudo, JsonElement Operation);

    public sealed record EnviarOperacaoPericialRequest(JsonElement Operation);

    public sealed record PrepararRompimentoLacreRequest(string? Justificativa);

    public sealed record EnviarOperacaoRompimentoLacreRequest(string? Justificativa, JsonElement Operation);

    public sealed record PrepararConsumoOuExaurimentoRequest(
        string? Tipo,
        string? QuantidadeDescrita,
        string? Justificativa);

    public sealed record EnviarOperacaoConsumoOuExaurimentoRequest(
        string? Tipo,
        string? QuantidadeDescrita,
        string? Justificativa,
        JsonElement Operation);
    public sealed record PrepararFracionamentoRequest(string? RotuloEvidenciaResultante, string? DescricaoResultante, string? QuantidadeDescrita, string? Justificativa);
    public sealed record EnviarOperacaoFracionamentoRequest(string? RotuloEvidenciaResultante, string? DescricaoResultante, string? QuantidadeDescrita, string? Justificativa, JsonElement Operation);
    public sealed record PrepararUnificacaoRequest(string? OutrosVestigiosOrigemIds, string? RotuloEvidenciaResultante, string? DescricaoResultante, string? Justificativa);
    public sealed record EnviarOperacaoUnificacaoRequest(string? OutrosVestigiosOrigemIds, string? RotuloEvidenciaResultante, string? DescricaoResultante, string? Justificativa, JsonElement Operation);
}
