using System.Security.Cryptography;
using CustodyChain.Web.Application.Auditoria;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

public class AuditoriaController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
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

        // RF15: histórico lido diretamente do ledger Hyperledger Fabric —
        // cada evento é uma transação imutável gravada pelo chaincode
        // CustodyChain (HistoricoRegistro), não uma fila de ancoragem
        // local. Resolve a limitação registrada em
        // v0.15.1-limitacao-verificador-le-do-banco.md: a linha do tempo
        // passa a depender do ledger real, não do MySQL.
        var eventos = await ledger.HistoricoRegistroAsync(vestigio.Id.ToString());
        modelo.Eventos = eventos
            .Select(e => new EventoLinhaDoTempoViewModel(e.Estado, e.OcorridoEm, e.DidResponsavel))
            .ToList();

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

        var operacaoColetaId = await db.RegistrosLedger
            .Where(r => r.VestigioId == vestigio.Id
                        && r.Evento == "COLETA_REGISTRAR"
                        && r.Estado == Models.Entities.EstadoRegistroLedger.ANCORADO
                        && r.OperacaoAssinadaId != null)
            .OrderByDescending(r => r.AncoradoEm)
            .Select(r => r.OperacaoAssinadaId)
            .FirstOrDefaultAsync();

        if (operacaoColetaId is not null)
        {
            return await VerificarColetaAssinadaAsync(modelo, vestigio, operacaoColetaId);
        }

        return await VerificarCoCHistoricaAsync(modelo, vestigio.Id);
    }

    private async Task<IActionResult> VerificarColetaAssinadaAsync(
        VerificadorViewModel modelo,
        Models.Entities.Vestigio vestigio,
        string operacaoColetaId)
    {
        AtestacaoIntegridadeOperacao? atestacao;
        try
        {
            var registrada = await ledger.ObterOperacaoAssinadaV1Async(operacaoColetaId);
            atestacao = LeitorAtestacaoIntegridadeOperacao.LerColeta(
                registrada.SignedOperation,
                vestigio.AssetRef ?? string.Empty,
                vestigio.RotuloEvidencia);
        }
        catch (Exception)
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "Não foi possível consultar a operação assinada no ledger para conferir este vestígio. Tente novamente mais tarde.";
            return View(nameof(Verificador), modelo);
        }

        if (atestacao is null)
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "Este vestígio não possui atestação de conteúdo assinada para conferência.";
            return View(nameof(Verificador), modelo);
        }

        var hashCalculado = await CalcularHashArquivoAsync(modelo.Arquivo!);
        modelo.HashCalculado = hashCalculado;
        modelo.HashRegistrado = atestacao.ContentHashSha256;
        modelo.Conferido = hashCalculado == atestacao.ContentHashSha256;
        modelo.MensagemResultado = modelo.Conferido.Value
            ? "O hash do arquivo confere com a atestação assinada registrada no ledger."
            : "O hash do arquivo NÃO confere com a atestação assinada registrada no ledger. A integridade não pode ser comprovada.";
        return View(nameof(Verificador), modelo);
    }

    private async Task<IActionResult> VerificarCoCHistoricaAsync(VerificadorViewModel modelo, long vestigioId)
    {
        // CoC é um fallback exclusivo para registros que antecedem a coleta
        // assinada; novos registros nunca usam esta fonte de integridade.
        var identificadorCredencial = await db.Credenciais
            .Where(c => c.VestigioId == vestigioId && c.Tipo == Models.Entities.TipoCredencial.COC)
            .OrderByDescending(c => c.EmitidaEm)
            .Select(c => c.Identificador)
            .FirstOrDefaultAsync();

        if (identificadorCredencial is null)
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "Este vestígio não possui atestação de conteúdo verificável no ledger.";
            return View(nameof(Verificador), modelo);
        }

        // RF (verificador independente): o hash a comparar vem direto do
        // ledger Hyperledger Fabric (ObterCredencialCoC), não mais de
        // VESTIGIO.HashSha256 no MySQL. Resolve a limitação registrada em
        // v0.15.1-limitacao-verificador-le-do-banco.md — quem administra
        // o banco não pode mais forjar essa conferência sozinho.
        CredencialCoCRegistrada credencial;
        try
        {
            credencial = await ledger.ObterCredencialCoCAsync(identificadorCredencial);
        }
        catch (Exception)
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "Não foi possível consultar o ledger para conferir este vestígio. Tente novamente mais tarde.";
            return View(nameof(Verificador), modelo);
        }

        var hashCalculado = await CalcularHashArquivoAsync(modelo.Arquivo!);

        modelo.HashCalculado = hashCalculado;
        modelo.HashRegistrado = credencial.PayloadHashSha256;

        if (credencial.Revogada)
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "A credencial de cadeia de custódia deste vestígio foi revogada no ledger. A integridade não pode ser comprovada.";
        }
        else if (string.IsNullOrEmpty(credencial.PayloadHashSha256))
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "Este vestígio não tem hash registrado no ledger para conferência.";
        }
        else if (hashCalculado == credencial.PayloadHashSha256)
        {
            modelo.Conferido = true;
            modelo.MensagemResultado = "O hash do arquivo confere com o hash registrado no ledger para este vestígio.";
        }
        else
        {
            modelo.Conferido = false;
            modelo.MensagemResultado = "O hash do arquivo NÃO confere com o hash registrado no ledger. A integridade não pode ser comprovada.";
        }

        return View(nameof(Verificador), modelo);
    }

    private static async Task<string> CalcularHashArquivoAsync(IFormFile arquivo)
    {
        await using var stream = arquivo.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexStringLower(hash);
    }
}
