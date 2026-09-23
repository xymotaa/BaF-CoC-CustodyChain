using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class VestigiosController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
{
    [HttpGet("/vestigios/cadastrar")]
    public async Task<IActionResult> Cadastrar()
    {
        var modelo = new CadastroVestigioViewModel();
        await CarregarOpcoesAsync(modelo);
        return View(modelo);
    }

    [HttpPost("/vestigios/cadastrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cadastrar(CadastroVestigioViewModel modelo)
    {
        if (modelo.RotuloEvidencia is not null &&
            await db.Vestigios.AnyAsync(v => v.RotuloEvidencia == modelo.RotuloEvidencia))
        {
            ModelState.AddModelError(nameof(modelo.RotuloEvidencia), "Já existe um vestígio com este rótulo de evidência.");
        }

        if (!ModelState.IsValid)
        {
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }

        var criadorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var agora = DateTime.UtcNow;

        var vestigio = new Vestigio
        {
            RotuloEvidencia = modelo.RotuloEvidencia!,
            RotuloConjunto = modelo.RotuloConjunto!,
            NumeroEvidencia = modelo.NumeroEvidencia,
            ProcessoId = modelo.ProcessoId!.Value,
            TipoVestigioId = modelo.TipoVestigioId!.Value,
            Descricao = modelo.Descricao!,
            CriadorId = criadorId,
            CustodianteAtualId = criadorId,
            LocalColeta = modelo.LocalColeta,
            DataHoraColeta = modelo.DataHoraColeta!.Value.ToUniversalTime(),
            MetodoColeta = modelo.MetodoColeta,
            HouveIntercorrencia = modelo.HouveIntercorrencia,
            DescricaoIntercorrencia = modelo.HouveIntercorrencia ? modelo.DescricaoIntercorrencia : null,
            EtapaAtual = 4, // Coleta, art. 158-B
            FaseAtual = FaseVestigio.EXTERNA,
            Estado = EstadoVestigio.Coletado,
            CriadoEm = agora,
        };

        db.Vestigios.Add(vestigio);
        await db.SaveChangesAsync();

        var lacre = new Lacre
        {
            VestigioId = vestigio.Id,
            Numero = modelo.NumeroLacre!,
            Situacao = SituacaoLacre.INTACTO,
            AplicadoPorId = criadorId,
            AplicadoEm = agora,
        };
        db.Lacres.Add(lacre);
        await db.SaveChangesAsync();

        // Lacre Digital (Quadro 34, Seção 10.2): os sete campos mínimos mais
        // o hash SHA-256 e o número do lacre físico, extensões deste projeto
        // (decisão D-13). É o payload mínimo gravado na credencial de CoC.
        var processo = await db.Processos.FindAsync(vestigio.ProcessoId);
        var criador = await db.Intervenientes.FindAsync(criadorId);

        var lacreDigital = new
        {
            NE = vestigio.NumeroEvidencia,
            CRI = criador!.Did,
            DE = vestigio.Descricao,
            NC = processo!.Numero,
            DH = vestigio.DataHoraColeta,
            RE = vestigio.RotuloEvidencia,
            RC = vestigio.RotuloConjunto,
            NumeroLacre = lacre.Numero,
        };
        var payloadJson = JsonSerializer.Serialize(lacreDigital);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        vestigio.HashSha256 = hashPayload;

        // Credencial #1 (Quadro 14): criação, no ato da coleta.
        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), "COLETA", criador.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = criadorId,
            EmissorId = criadorId,
            ProcessoId = vestigio.ProcessoId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        // Escrita no ledger é assíncrona por design (~2,3s por emissão,
        // medido por Loffi): grava PENDENTE aqui, um worker consome a fila
        // depois. O LedgerFake já respondeu acima; produção troca só a
        // implementação de IServicoLedger, sem alterar este controller.
        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "VESTIGIO",
            RegistroOrigemId = vestigio.Id,
            VestigioId = vestigio.Id,
            Evento = "COLETA",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Vestígio {vestigio.RotuloEvidencia} cadastrado. Credencial de cadeia de custódia emitida.";
        return RedirectToAction(nameof(Cadastrar));
    }

    private async Task CarregarOpcoesAsync(CadastroVestigioViewModel modelo)
    {
        modelo.ProcessosDisponiveis = await db.Processos
            .Where(p => p.Situacao == Models.Entities.SituacaoProcesso.ATIVO)
            .OrderBy(p => p.Numero)
            .Select(p => new OpcaoProcessoViewModel(p.Id, p.Numero, p.NomeOperacao))
            .ToListAsync();

        modelo.TiposDisponiveis = await db.TiposVestigio
            .OrderBy(t => t.Descricao)
            .Select(t => new OpcaoTipoVestigioViewModel(t.Id, t.Descricao, t.Categoria.ToString()))
            .ToListAsync();
    }
}
