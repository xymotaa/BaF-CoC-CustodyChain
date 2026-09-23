using System.Security.Claims;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

// RN07: somente administradores cadastram intervenientes e emitem
// credencial de permissão.
[Authorize(Roles = "ADMIN")]
public class GestaoPerfisController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
{
    private static readonly Dictionary<string, TipoAtor> MapaPerfilParaAtor = new()
    {
        ["ADMIN"] = TipoAtor.Admin,
        ["CUSTODIA"] = TipoAtor.Custodian,
        ["COLETOR"] = TipoAtor.Delegate,
        ["PERITO"] = TipoAtor.Expert,
        ["EXTERNO"] = TipoAtor.Judge,
    };

    [HttpGet("/gestao-perfis")]
    public async Task<IActionResult> Index()
    {
        var intervenientes = await db.Intervenientes
            .Include(i => i.Perfil)
            .OrderByDescending(i => i.CriadoEm)
            .Select(i => new ItemIntervenienteViewModel(
                i.Id, i.Did, i.Nome, i.Perfil.Nome, i.Situacao.ToString(), i.CriadoEm, i.AtivadoEm))
            .ToListAsync();

        return View(new GestaoPerfisListaViewModel { Intervenientes = intervenientes });
    }

    [HttpGet("/gestao-perfis/cadastrar")]
    public async Task<IActionResult> Cadastrar()
    {
        var modelo = new CadastrarIntervenienteViewModel();
        await CarregarPerfisAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/gestao-perfis/cadastrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cadastrar(CadastrarIntervenienteViewModel modelo)
    {
        var perfil = modelo.PerfilId is not null
            ? await db.Perfis.FirstOrDefaultAsync(p => p.Id == modelo.PerfilId)
            : null;

        if (modelo.PerfilId is not null && perfil is null)
        {
            ModelState.AddModelError(nameof(modelo.PerfilId), "Perfil não encontrado.");
        }

        if (!ModelState.IsValid || perfil is null)
        {
            await CarregarPerfisAsync(modelo);
            return View(modelo);
        }

        // RF03: gerar DID — a ativação (com assinatura do emissor) fica
        // pendente até a etapa seguinte, na lista de intervenientes.
        var tipoAtor = MapaPerfilParaAtor[perfil.Codigo];
        var did = await ledger.GerarDidAsync(tipoAtor);

        db.Intervenientes.Add(new Interveniente
        {
            Did = did,
            PerfilId = perfil.Id,
            Nome = modelo.Nome!.Trim(),
            Matricula = modelo.Matricula,
            Orgao = modelo.Orgao,
            Lotacao = modelo.Lotacao,
            Situacao = SituacaoInterveniente.GERADO,
            CriadoEm = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Interveniente {modelo.Nome} cadastrado com DID {did}. Aguardando ativação.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/gestao-perfis/ativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ativar(long intervenienteId)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var emissor = await db.Intervenientes.FindAsync(emissorId);

        var interveniente = await db.Intervenientes
            .FirstOrDefaultAsync(i => i.Id == intervenienteId && i.Situacao == SituacaoInterveniente.GERADO);

        if (interveniente is null)
        {
            TempData["MensagemErro"] = "Interveniente não encontrado ou já ativado.";
            return RedirectToAction(nameof(Index));
        }

        // RF03: ativação com assinatura do emissor (o Administrador
        // logado). No LedgerFake a "senha do emissor" é simbólica — a
        // wallet real assinaria com a chave Ed25519, fora do servidor.
        await ledger.AtivarDidAsync(interveniente.Did, emissor!.Did, senhaEmissor: "");

        interveniente.Situacao = SituacaoInterveniente.ATIVO;
        interveniente.DidEmissor = emissor.Did;
        interveniente.AtivadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"{interveniente.Nome} ativado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/gestao-perfis/revogar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revogar(long intervenienteId)
    {
        var interveniente = await db.Intervenientes
            .FirstOrDefaultAsync(i => i.Id == intervenienteId && i.Situacao == SituacaoInterveniente.ATIVO);

        if (interveniente is null)
        {
            TempData["MensagemErro"] = "Interveniente não encontrado ou não está ativo.";
            return RedirectToAction(nameof(Index));
        }

        interveniente.Situacao = SituacaoInterveniente.REVOGADO;
        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"{interveniente.Nome} revogado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/gestao-perfis/emitir-credencial")]
    public async Task<IActionResult> EmitirCredencial()
    {
        var modelo = new EmitirCredencialPermissaoViewModel();
        await CarregarOpcoesCredencialAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/gestao-perfis/emitir-credencial")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmitirCredencial(EmitirCredencialPermissaoViewModel modelo)
    {
        var emissorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var titular = modelo.TitularId is not null
            ? await db.Intervenientes.Include(i => i.Perfil)
                .FirstOrDefaultAsync(i => i.Id == modelo.TitularId && i.Situacao == SituacaoInterveniente.ATIVO)
            : null;

        if (modelo.TitularId is not null && titular is null)
        {
            ModelState.AddModelError(nameof(modelo.TitularId), "Titular não encontrado ou não está ativo.");
        }

        if (!ModelState.IsValid || titular is null)
        {
            await CarregarOpcoesCredencialAsync(modelo);
            return View(modelo);
        }

        var emissor = await db.Intervenientes.FindAsync(emissorId);
        var agora = DateTime.UtcNow;

        var credencialId = await ledger.EmitirCredencialPermissaoAsync(
            new CredencialPermissaoDto(titular.Did, emissor!.Did, titular.Perfil.Codigo));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.PERMISSAO,
            Identificador = credencialId,
            TitularId = titular.Id,
            EmissorId = emissorId,
            ProcessoId = modelo.ProcessoId,
            EmitidaEm = agora,
            ValidaAte = modelo.ValidaAte,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Credencial de permissão emitida para {titular.Nome}.";
        return RedirectToAction(nameof(EmitirCredencial));
    }

    private async Task CarregarPerfisAsync(CadastrarIntervenienteViewModel modelo)
    {
        modelo.PerfisDisponiveis = await db.Perfis
            .OrderBy(p => p.Nome)
            .Select(p => new OpcaoPerfilViewModel(p.Id, p.Codigo, p.Nome))
            .ToListAsync();
    }

    private async Task CarregarOpcoesCredencialAsync(EmitirCredencialPermissaoViewModel modelo)
    {
        modelo.TitularesDisponiveis = await db.Intervenientes
            .Include(i => i.Perfil)
            .Where(i => i.Situacao == SituacaoInterveniente.ATIVO)
            .OrderBy(i => i.Nome)
            .Select(i => new ItemIntervenienteViewModel(
                i.Id, i.Did, i.Nome, i.Perfil.Nome, i.Situacao.ToString(), i.CriadoEm, i.AtivadoEm))
            .ToListAsync();

        modelo.ProcessosDisponiveis = await db.Processos
            .OrderBy(p => p.Numero)
            .Select(p => new OpcaoProcessoViewModel(p.Id, p.Numero, p.NomeOperacao))
            .ToListAsync();
    }
}
