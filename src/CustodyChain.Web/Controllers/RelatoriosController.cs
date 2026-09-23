using System.Security.Claims;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Relatorios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class RelatoriosController(CustodyChainDbContext db) : Controller
{
    [HttpGet("/relatorios/processo")]
    public async Task<IActionResult> Processo(long? processoId)
    {
        var modelo = await MontarModeloAsync(processoId);
        return View(modelo);
    }

    [HttpGet("/relatorios/processo/pdf")]
    public async Task<IActionResult> ProcessoPdf(long processoId)
    {
        var modelo = await MontarModeloAsync(processoId);
        if (modelo.ProcessoId is null)
        {
            return NotFound();
        }

        var pdf = GeradorRelatorioProcessoPdf.Gerar(modelo);
        var nomeArquivo = $"relatorio-{NormalizarNomeArquivo(modelo.ProcessoNumero!)}.pdf";
        return File(pdf, "application/pdf", nomeArquivo);
    }

    [HttpGet("/relatorios/processo/xml")]
    public async Task<IActionResult> ProcessoXml(long processoId)
    {
        var modelo = await MontarModeloAsync(processoId);
        if (modelo.ProcessoId is null)
        {
            return NotFound();
        }

        var xml = GeradorRelatorioProcessoXml.Gerar(modelo);
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        var nomeArquivo = $"relatorio-{NormalizarNomeArquivo(modelo.ProcessoNumero!)}.xml";
        return File(bytes, "application/xml", nomeArquivo);
    }

    private async Task<RelatorioProcessoViewModel> MontarModeloAsync(long? processoId)
    {
        var modelo = new RelatorioProcessoViewModel
        {
            GeradoEm = DateTime.UtcNow,
            ProcessosDisponiveis = await db.Processos
                .OrderBy(p => p.Numero)
                .Select(p => new OpcaoProcessoViewModel(p.Id, p.Numero, p.NomeOperacao))
                .ToListAsync(),
        };

        var geradorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (geradorId is not null)
        {
            var gerador = await db.Intervenientes.FindAsync(long.Parse(geradorId));
            modelo.GeradoPorNome = gerador?.Nome;
        }

        if (processoId is null)
        {
            return modelo;
        }

        var processo = await db.Processos.FirstOrDefaultAsync(p => p.Id == processoId);
        if (processo is null)
        {
            return modelo;
        }

        modelo.ProcessoId = processo.Id;
        modelo.ProcessoNumero = processo.Numero;
        modelo.ProcessoNomeOperacao = processo.NomeOperacao;
        modelo.ProcessoOrgaoOrigem = processo.OrgaoOrigem;
        modelo.ProcessoDataAbertura = processo.DataAbertura;
        modelo.ProcessoSituacao = processo.Situacao.ToString();

        modelo.Vestigios = await db.Vestigios
            .Include(v => v.TipoVestigio)
            .Include(v => v.CustodianteAtual)
            .Where(v => v.ProcessoId == processoId)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new ItemRelatorioVestigioViewModel(
                v.RotuloEvidencia, v.RotuloConjunto, v.TipoVestigio.Descricao, v.Descricao,
                v.Estado.ToString(), v.CustodianteAtual != null ? v.CustodianteAtual.Nome : null,
                v.DataHoraColeta, v.CriadoEm, v.HashSha256))
            .ToListAsync();

        return modelo;
    }

    private static string NormalizarNomeArquivo(string numeroProcesso) =>
        numeroProcesso.Replace(".", "-").Replace("/", "-");
}
