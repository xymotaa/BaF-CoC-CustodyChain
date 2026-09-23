using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Armazenamento;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class DestinacaoFinalController(
    CustodyChainDbContext db,
    IServicoLedger ledger,
    IServicoArmazenamentoArquivos armazenamento) : Controller
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

    [HttpPost("/destinacao-final/solicitar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Solicitar(SolicitarDestinacaoViewModel modelo)
    {
        var solicitanteId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var vestigio = modelo.VestigioId is not null
            ? await db.Vestigios.FirstOrDefaultAsync(v =>
                v.Id == modelo.VestigioId
                && (v.Estado == EstadoVestigio.Armazenado || v.Estado == EstadoVestigio.Periciado))
            : null;

        if (modelo.VestigioId is not null && vestigio is null)
        {
            ModelState.AddModelError(nameof(modelo.VestigioId), "Vestígio não encontrado ou não está em um estado elegível para destinação final.");
        }

        if (!Enum.TryParse<TipoDescarte>(modelo.Tipo, out var tipo))
        {
            ModelState.AddModelError(nameof(modelo.Tipo), "Tipo inválido.");
        }

        if (modelo.ArquivoAutorizacao is null || modelo.ArquivoAutorizacao.Length == 0)
        {
            ModelState.AddModelError(nameof(modelo.ArquivoAutorizacao), "Anexe o mandado judicial.");
        }

        if (!ModelState.IsValid || vestigio is null)
        {
            await CarregarOpcoesAsync(modelo);
            return View(modelo);
        }

        var agora = DateTime.UtcNow;

        // RN20: a destinação final exige autorização judicial anexada.
        // O arquivo é hasheado antes do upload — o hash gravado em ANEXO
        // é a garantia de integridade, independente de onde o conteúdo
        // acabe armazenado.
        byte[] bytesArquivo;
        using (var memoria = new MemoryStream())
        {
            await modelo.ArquivoAutorizacao!.CopyToAsync(memoria);
            bytesArquivo = memoria.ToArray();
        }
        var hashArquivo = Convert.ToHexStringLower(SHA256.HashData(bytesArquivo));

        ArquivoArmazenado armazenado;
        using (var streamUpload = new MemoryStream(bytesArquivo))
        {
            armazenado = await armazenamento.ArmazenarAsync(streamUpload, modelo.ArquivoAutorizacao!.FileName);
        }

        var anexo = new Anexo
        {
            VestigioId = vestigio.Id,
            Tipo = TipoAnexo.AUTORIZACAO,
            NomeArquivo = modelo.ArquivoAutorizacao!.FileName,
            CaminhoRelativo = armazenado.Cid,
            TamanhoBytes = armazenado.TamanhoBytes,
            HashSha256 = hashArquivo,
            Algoritmo = "SHA-256",
            EnviadoPorId = solicitanteId,
            EnviadoEm = agora,
        };
        db.Anexos.Add(anexo);
        await db.SaveChangesAsync();

        db.Descartes.Add(new Descarte
        {
            VestigioId = vestigio.Id,
            Tipo = tipo,
            AutorizacaoAnexoId = anexo.Id,
            DidMagistrado = modelo.DidMagistrado!.Trim(),
            SolicitadoPorId = solicitanteId,
            Observacao = modelo.Observacao,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Destinação final de {vestigio.RotuloEvidencia} solicitada. Aguardando aprovação.";
        return RedirectToAction(nameof(Solicitar));
    }

    [HttpPost("/destinacao-final/aprovar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aprovar(long descarteId)
    {
        var aprovadorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var descarte = await db.Descartes
            .Include(d => d.Vestigio)
            .FirstOrDefaultAsync(d => d.Id == descarteId && d.AprovadoPorId == null && d.ExecutadoEm == null);

        // RN16: quem solicitou não pode aprovar.
        if (descarte is null || descarte.SolicitadoPorId == aprovadorId)
        {
            TempData["MensagemErro"] = "Destinação não encontrada, ou quem solicitou não pode aprovar (RN16).";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        var vestigio = descarte.Vestigio;

        descarte.AprovadoPorId = aprovadorId;
        descarte.ExecutadoEm = agora;

        // RN11: o descarte encerra o ciclo do objeto físico; o registro
        // digital permanece perpétuo, nunca excluído. Restituição também
        // encerra a cadeia de custódia do vestígio (ele sai da guarda),
        // mesmo estado final na máquina de estados.
        vestigio.Estado = EstadoVestigio.Descartado;
        vestigio.EtapaAtual = 10; // Descarte, art. 158-B
        vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        var aprovador = await db.Intervenientes.FindAsync(aprovadorId);
        var payload = new
        {
            RE = vestigio.RotuloEvidencia,
            Tipo = descarte.Tipo.ToString(),
            DidMagistrado = descarte.DidMagistrado,
            AprovadoPor = aprovador!.Did,
            ExecutadoEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        // Credencial #7 (Quadro 14): encerramento.
        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), "ENCERRAMENTO", aprovador.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = aprovadorId,
            EmissorId = aprovadorId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "DESCARTE",
            RegistroOrigemId = descarte.Id,
            VestigioId = vestigio.Id,
            Evento = "ENCERRAMENTO",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"{(descarte.Tipo == TipoDescarte.DESCARTE ? "Descarte" : "Restituição")} de {vestigio.RotuloEvidencia} aprovado e executado. Cadeia encerrada.";
        return RedirectToAction(nameof(Index));
    }

    private async Task CarregarOpcoesAsync(SolicitarDestinacaoViewModel modelo)
    {
        modelo.VestigiosDisponiveis = await db.Vestigios
            .Where(v => v.Estado == EstadoVestigio.Armazenado || v.Estado == EstadoVestigio.Periciado)
            .OrderBy(v => v.RotuloEvidencia)
            .Select(v => new OpcaoVestigioViewModel(v.Id, v.RotuloEvidencia, v.Descricao))
            .ToListAsync();
    }
}
