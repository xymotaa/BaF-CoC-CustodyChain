using System.Security.Claims;
using System.Text.Json;
using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustodyChain.Web.Controllers;

[Authorize(Policy = PoliticasAutorizacao.CadastrarVestigio)]
public class VestigiosController(
    ICadastrarVestigio cadastrarVestigio,
    ICadastroVestigioOpcoesQuery opcoesQuery,
    IConfiguration configuration) : Controller
{
    [HttpGet("/vestigios/cadastrar")]
    public async Task<IActionResult> Cadastrar(CancellationToken cancellationToken)
    {
        var modelo = new CadastroVestigioViewModel();
        await CarregarOpcoesAsync(modelo, cancellationToken);
        return View(modelo);
    }

    [HttpPost("/vestigios/cadastrar/comando")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepararCadastro(
        [FromForm] CadastroVestigioViewModel? modelo,
        CancellationToken cancellationToken)
    {
        if (modelo is null || !TryObterCriador(out var criadorId))
            return Forbid();

        try
        {
            var preparacao = await cadastrarVestigio.PrepararAsync(
                CriarCommand(
                    criadorId,
                    modelo,
                    await CriarArquivoEvidenciaAsync(modelo.ArquivoEvidencia, cancellationToken)),
                cancellationToken);

            return Ok(new
            {
                operation = preparacao.Operacao,
                signerDid = preparacao.DidColetor,
                walletEndpoint = configuration["AuthenticationDid:WalletEndpoint"] ?? "http://127.0.0.1:43123"
            });
        }
        catch (AtorCadastroVestigioNaoAutorizadoException)
        {
            return Forbid();
        }
        catch (Exception exception) when (exception is ValidacaoCadastroVestigioException
                                          or ConflitoCadastroVestigioException
                                          or RecursoCadastroVestigioNaoEncontradoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("/vestigios/cadastrar/prova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConcluirCadastro(
        [FromBody] CadastroVestigioComProvaRequest? requisicao,
        CancellationToken cancellationToken)
    {
        if (requisicao is null || !TryObterCriador(out var criadorId))
            return Forbid();

        try
        {
            var resultado = await cadastrarVestigio.ExecutarAsync(
                new ConcluirCadastroVestigioCommand(
                    CriarCommand(criadorId, requisicao.Cadastro),
                    requisicao.Operation),
                cancellationToken);

            TempData["MensagemSucesso"] =
                $"Vestígio {resultado.RotuloEvidencia} cadastrado e confirmado no ledger.";
            return Ok(new { redirectUrl = Url.Action(nameof(Cadastrar)) });
        }
        catch (AtorCadastroVestigioNaoAutorizadoException)
        {
            return Forbid();
        }
        catch (IndisponibilidadeLedgerCadastroVestigioException exception)
        {
            return StatusCode(503, new { message = exception.Message });
        }
        catch (Exception exception) when (exception is ValidacaoCadastroVestigioException
                                          or ConflitoCadastroVestigioException
                                          or RecursoCadastroVestigioNaoEncontradoException)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private bool TryObterCriador(out long criadorId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out criadorId);

    private static CadastrarVestigioCommand CriarCommand(long criadorId, CadastroVestigioViewModel modelo) =>
        CriarCommand(criadorId, modelo, null);

    private static CadastrarVestigioCommand CriarCommand(
        long criadorId,
        CadastroVestigioViewModel modelo,
        ArquivoEvidenciaColeta? arquivoEvidencia) =>
        new(
            criadorId,
            modelo.RotuloEvidencia ?? string.Empty,
            modelo.RotuloConjunto ?? string.Empty,
            modelo.NumeroEvidencia,
            modelo.ProcessoId ?? 0,
            modelo.TipoVestigioId ?? 0,
            modelo.Descricao ?? string.Empty,
            modelo.LocalColeta,
            modelo.DataHoraColeta ?? default,
            modelo.MetodoColeta,
            modelo.NumeroLacre ?? string.Empty,
            modelo.HouveIntercorrencia,
            modelo.DescricaoIntercorrencia,
            arquivoEvidencia);

    private static async Task<ArquivoEvidenciaColeta?> CriarArquivoEvidenciaAsync(
        IFormFile? arquivo,
        CancellationToken cancellationToken)
    {
        if (arquivo is not { Length: > 0 }) return null;

        await using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, cancellationToken);
        return new ArquivoEvidenciaColeta(
            Path.GetFileName(arquivo.FileName),
            string.IsNullOrWhiteSpace(arquivo.ContentType) ? "application/octet-stream" : arquivo.ContentType,
            memoria.ToArray());
    }

    private async Task CarregarOpcoesAsync(CadastroVestigioViewModel modelo, CancellationToken cancellationToken)
    {
        var processos = await opcoesQuery.ListarProcessosAtivosAsync(cancellationToken);
        var tipos = await opcoesQuery.ListarTiposAsync(cancellationToken);
        modelo.ProcessosDisponiveis = processos
            .Select(p => new OpcaoProcessoViewModel(p.Id, p.Numero, p.NomeOperacao)).ToList();
        modelo.TiposDisponiveis = tipos
            .Select(t => new OpcaoTipoVestigioViewModel(t.Id, t.Descricao, t.Categoria)).ToList();
    }
}

public sealed record CadastroVestigioComProvaRequest(
    CadastroVestigioViewModel Cadastro,
    JsonElement Operation);
