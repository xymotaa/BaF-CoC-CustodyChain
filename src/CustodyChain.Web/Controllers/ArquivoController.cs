using System.Security.Claims;
using System.Text.Json;
using CustodyChain.Web.Application.Arquivo;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class ArquivoController(CustodyChainDbContext db, IDarEntradaArquivo darEntradaArquivo, IConfiguration configuration) : Controller
{
    [HttpGet("/arquivo")]
    public async Task<IActionResult> Index(string? busca, string? categoria)
    {
        var query = db.Vestigios
            .Include(v => v.Processo)
            .Include(v => v.TipoVestigio)
            .Include(v => v.CustodianteAtual)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim();
            query = query.Where(v =>
                v.RotuloEvidencia.Contains(termo) ||
                v.HashSha256 != null && v.HashSha256.Contains(termo) ||
                v.Processo.Numero.Contains(termo) ||
                (v.CustodianteAtual != null && v.CustodianteAtual.Nome.Contains(termo)));
        }

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            query = query.Where(v => v.TipoVestigio.Categoria.ToString() == categoria);
        }

        var vestigios = await query
            .OrderByDescending(v => v.CriadoEm)
            .ToListAsync();

        var vestigioIds = vestigios.Select(v => v.Id).ToList();
        var armazenamentos = await db.Armazenamentos
            .Where(a => vestigioIds.Contains(a.VestigioId) && a.Situacao == SituacaoArmazenamento.GUARDADO)
            .ToDictionaryAsync(a => a.VestigioId);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var itens = vestigios.Select(v =>
        {
            armazenamentos.TryGetValue(v.Id, out var armazenamento);
            return new ItemArquivoViewModel(
                v.Id, v.RotuloEvidencia, v.RotuloConjunto, v.Descricao,
                v.TipoVestigio.Descricao, v.TipoVestigio.Categoria.ToString(),
                v.Processo.Numero, v.CustodianteAtual?.Nome, v.Estado.ToString(),
                v.HashSha256 ?? "", armazenamento?.Central, armazenamento?.Posicao,
                armazenamento?.PrazoGuardaAte,
                armazenamento?.PrazoGuardaAte is not null && armazenamento.PrazoGuardaAte < hoje);
        }).ToList();

        var categorias = await db.TiposVestigio
            .Select(t => t.Categoria.ToString())
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return View(new ArquivoListaViewModel
        {
            Busca = busca,
            CategoriaFiltro = categoria,
            Itens = itens,
            Categorias = categorias,
        });
    }

    [HttpGet("/arquivo/entrada")]
    public async Task<IActionResult> Entrada()
    {
        var modelo = new DarEntradaArquivoViewModel();
        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/arquivo/entrada/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararEntrada(
        [FromBody] DarEntradaArquivoViewModel? modelo,
        CancellationToken cancellationToken)
    {
        if (modelo is null) return BadRequest(new { message = "Informe os dados da guarda." });
        if (!TryObterIntervenienteId(out var recebedorId))
            return Forbid();

        try
        {
            var preparacao = await darEntradaArquivo.PrepararAsync(CriarCommand(recebedorId, modelo), cancellationToken);
            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidSignatario,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (AtorEntradaArquivoNaoAutorizadoException) { return Forbid(); }
        catch (Exception exception) when (exception is ValidacaoEntradaArquivoException
            or RecursoEntradaArquivoNaoEncontradoException or ConflitoEntradaArquivoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/arquivo/entrada/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirEntrada(
        [FromBody] EntradaArquivoComProvaRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null) return BadRequest(new { message = "Informe a prova da guarda." });
        if (!TryObterIntervenienteId(out var recebedorId)) return Forbid();
        try
        {
            var resultado = await darEntradaArquivo.ExecutarAsync(new ConcluirEntradaArquivoCommand(
                CriarCommand(recebedorId, requisicao.Entrada), requisicao.Operation), cancellationToken);
            TempData["MensagemSucesso"] = $"Vestígio {resultado.RotuloEvidencia} arquivado e confirmado no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Entrada)) });
        }
        catch (AtorEntradaArquivoNaoAutorizadoException) { return Forbid(); }
        catch (IndisponibilidadeLedgerEntradaArquivoException exception) { return StatusCode(503, new { message = exception.Message }); }
        catch (Exception exception) when (exception is ValidacaoEntradaArquivoException
            or RecursoEntradaArquivoNaoEncontradoException or ConflitoEntradaArquivoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private static DarEntradaArquivoCommand CriarCommand(long recebedorId, DarEntradaArquivoViewModel modelo) =>
        new(recebedorId, modelo.VestigioId ?? 0, modelo.Central, modelo.Posicao, modelo.PrazoGuardaAte);

    private async Task CarregarOpcoesAsync(DarEntradaArquivoViewModel modelo)
    {
        var recebedorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        modelo.VestigiosDisponiveis = await db.Vestigios
            .Where(v => v.Estado == EstadoVestigio.Recebido && v.CustodianteAtualId == recebedorId)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync();
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;
}

public sealed record EntradaArquivoComProvaRequest(DarEntradaArquivoViewModel Entrada, JsonElement Operation);
