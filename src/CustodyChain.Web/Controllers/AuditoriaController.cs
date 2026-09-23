using System.Security.Cryptography;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

public class AuditoriaController(CustodyChainDbContext db) : Controller
{
    // Linha do tempo: dentro do sistema, exige autenticação (é onde os
    // intervenientes consultam o histórico de um vestígio).
    [Authorize]
    [HttpGet("/auditoria")]
    public async Task<IActionResult> LinhaDoTempo(string? busca)
    {
        var modelo = new LinhaDoTempoViewModel { Busca = busca };

        if (string.IsNullOrWhiteSpace(busca))
        {
            return View(modelo);
        }

        var termo = busca.Trim();
        var vestigio = await db.Vestigios
            .FirstOrDefaultAsync(v => v.RotuloEvidencia == termo);

        if (vestigio is null)
        {
            return View(modelo);
        }

        modelo.VestigioId = vestigio.Id;
        modelo.RotuloEvidencia = vestigio.RotuloEvidencia;
        modelo.Descricao = vestigio.Descricao;
        modelo.EstadoAtual = vestigio.Estado.ToString();
        modelo.HashSha256 = vestigio.HashSha256;

        // RF15: consultar o histórico completo do vestígio a partir do
        // registro de estados do ledger. REGISTRO_LEDGER é a fonte —
        // cada linha é um evento imutável, nunca alterado nem excluído
        // (RN03, RN11).
        modelo.Eventos = await db.RegistrosLedger
            .Where(r => r.VestigioId == vestigio.Id)
            .OrderBy(r => r.CriadoEm)
            .Select(r => new EventoLinhaDoTempoViewModel(
                r.Id, r.EntidadeOrigem, r.Evento, r.Estado.ToString(),
                r.CriadoEm, r.AncoradoEm, r.TxHash))
            .ToListAsync();

        return View(modelo);
    }

    // Verificador independente: sem login, "confere o hash de um
    // arquivo contra o ledger sem acesso ao sistema" (CONTEXTO-PROJETO).
    [AllowAnonymous]
    [HttpGet("/verificador")]
    public IActionResult Verificador()
    {
        return View(new VerificadorViewModel());
    }

    [AllowAnonymous]
    [HttpPost("/verificador")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verificador(VerificadorViewModel modelo)
    {
        if (!ModelState.IsValid)
        {
            return View(modelo);
        }

        var vestigio = await db.Vestigios
            .FirstOrDefaultAsync(v => v.RotuloEvidencia == modelo.RotuloEvidencia!.Trim());

        if (vestigio is null)
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "Nenhum vestígio encontrado com este rótulo de evidência.";
            return View(modelo);
        }

        byte[] bytesArquivo;
        using (var memoria = new MemoryStream())
        {
            await modelo.Arquivo!.CopyToAsync(memoria);
            bytesArquivo = memoria.ToArray();
        }
        var hashCalculado = Convert.ToHexStringLower(SHA256.HashData(bytesArquivo));

        modelo.HashCalculado = hashCalculado;
        modelo.HashRegistrado = vestigio.HashSha256;

        if (string.IsNullOrEmpty(vestigio.HashSha256))
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "Este vestígio não tem hash registrado para conferência.";
        }
        else if (hashCalculado == vestigio.HashSha256)
        {
            modelo.Conferido = true;
            modelo.MensagemResultado = "O hash do arquivo confere com o hash registrado para este vestígio.";
        }
        else
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "O hash do arquivo NÃO confere com o hash registrado. A integridade não pode ser comprovada.";
        }

        return View(modelo);
    }
}
