using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using CustodyChain.Web.Models.ViewModels;
using CustodyChain.Web.Services.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Controllers;

[Authorize]
public class ProcessamentoPericialController(
    CustodyChainDbContext db,
    IServicoLedger ledger,
    IRomperLacre romperLacre,
    IEmitirLaudo emitirLaudo,
    IFracionarAmostra fracionarAmostra) : Controller
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
    public async Task<IActionResult> Unificar(UnificarViewModel modelo)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .FirstOrDefaultAsync(p => p.Id == modelo.PericiaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.EM_EXECUCAO);

        var outrosIds = (modelo.OutrosVestigiosOrigemIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var id) ? id : (long?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (pericia is null
            || outrosIds.Count == 0
            || string.IsNullOrWhiteSpace(modelo.RotuloEvidenciaResultante)
            || string.IsNullOrWhiteSpace(modelo.DescricaoResultante)
            || string.IsNullOrWhiteSpace(modelo.Justificativa))
        {
            TempData["MensagemErro"] = "Perícia não encontrada, ou é preciso informar ao menos um outro vestígio e preencher os campos obrigatórios.";
            return RedirectToAction(nameof(Index));
        }

        var todosIds = outrosIds.Append(pericia.VestigioId).Distinct().ToList();
        var origens = await db.Vestigios
            .Where(v => todosIds.Contains(v.Id))
            .ToListAsync();

        // RN18: a unificação exige que todos os itens compartilhem o mesmo
        // rótulo de conjunto.
        if (origens.Count != todosIds.Count
            || origens.Select(v => v.RotuloConjunto).Distinct().Count() != 1)
        {
            TempData["MensagemErro"] = "Os vestígios informados precisam existir e compartilhar o mesmo rótulo de conjunto (RN18).";
            return RedirectToAction(nameof(Index));
        }

        if (await db.Vestigios.AnyAsync(v => v.RotuloEvidencia == modelo.RotuloEvidenciaResultante))
        {
            TempData["MensagemErro"] = "Já existe um vestígio com este rótulo de evidência.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        var rotuloConjunto = origens[0].RotuloConjunto;

        // Hash do item unificado: SHA-256 sobre a concatenação ordenada
        // (por rótulo de evidência) dos hashes das origens que o
        // compõem. Uma alteração em qualquer origem muda o resultante —
        // reflete que o item unificado carrega fielmente as partes.
        // Origens sem hash próprio (ex: vestígio cadastrado antes da
        // coluna existir) não contribuem para o cálculo.
        var hashesOrigens = origens
            .Where(o => !string.IsNullOrEmpty(o.HashSha256))
            .OrderBy(o => o.RotuloEvidencia)
            .Select(o => o.HashSha256)
            .ToList();
        var hashCombinado = hashesOrigens.Count > 0
            ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(hashesOrigens))))
            : null;

        var resultante = new Vestigio
        {
            RotuloEvidencia = modelo.RotuloEvidenciaResultante.Trim(),
            RotuloConjunto = rotuloConjunto,
            ProcessoId = origens[0].ProcessoId,
            TipoVestigioId = origens[0].TipoVestigioId,
            Descricao = modelo.DescricaoResultante.Trim(),
            CriadorId = peritoId,
            CustodianteAtualId = peritoId,
            HashSha256 = hashCombinado,
            EtapaAtual = 8,
            FaseAtual = FaseVestigio.INTERNA,
            Estado = EstadoVestigio.EmPericia,
            CriadoEm = agora,
        };
        db.Vestigios.Add(resultante);
        await db.SaveChangesAsync();

        // Um registro de OPERACAO_AMOSTRA por vestígio de origem — o
        // schema não tem uma FK N:1 pronta para múltiplas origens numa
        // linha só, então a linhagem completa fica expressa como várias
        // linhas apontando para o mesmo vestigio_resultante_id.
        var operacoes = origens.Select(origem => new OperacaoAmostra
        {
            PericiaId = pericia.Id,
            Tipo = TipoOperacaoAmostra.UNIFICACAO,
            VestigioOrigemId = origem.Id,
            VestigioResultanteId = resultante.Id,
            Justificativa = modelo.Justificativa.Trim(),
            ExecutadoPorId = peritoId,
            ExecutadoEm = agora,
        }).ToList();
        db.OperacoesAmostra.AddRange(operacoes);

        await db.SaveChangesAsync();

        // A operação lógica de unificação vira várias linhas em
        // OPERACAO_AMOSTRA (uma por origem); a primeira delas âncora o
        // único registro correspondente no ledger.
        await RegistrarEventoOperacaoAsync(operacoes[0].Id, resultante.Id, "UNIFICACAO", peritoId, new
        {
            REsOrigem = origens.Select(o => o.RotuloEvidencia),
            REResultante = resultante.RotuloEvidencia,
            RC = rotuloConjunto,
            Justificativa = modelo.Justificativa,
        });

        TempData["MensagemSucesso"] = $"{origens.Count} vestígios unificados em {resultante.RotuloEvidencia}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/consumir-exaurir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConsumirOuExaurir(ConsumirOuExaurirViewModel modelo)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .FirstOrDefaultAsync(p => p.Id == modelo.PericiaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.EM_EXECUCAO);

        if (pericia is null
            || !Enum.TryParse<TipoOperacaoAmostra>(modelo.Tipo, out var tipo)
            || (tipo != TipoOperacaoAmostra.CONSUMO && tipo != TipoOperacaoAmostra.EXAURIMENTO)
            || string.IsNullOrWhiteSpace(modelo.Justificativa))
        {
            TempData["MensagemErro"] = "Perícia não encontrada, lacre ainda não rompido, ou justificativa não informada.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        var vestigio = pericia.Vestigio;

        var operacao = new OperacaoAmostra
        {
            PericiaId = pericia.Id,
            Tipo = tipo,
            VestigioOrigemId = vestigio.Id,
            VestigioResultanteId = null,
            QuantidadeDescrita = modelo.QuantidadeDescrita,
            Justificativa = modelo.Justificativa.Trim(),
            ExecutadoPorId = peritoId,
            ExecutadoEm = agora,
        };
        db.OperacoesAmostra.Add(operacao);

        await db.SaveChangesAsync();

        await RegistrarEventoOperacaoAsync(operacao.Id, vestigio.Id, tipo.ToString(), peritoId, new
        {
            RE = vestigio.RotuloEvidencia,
            QuantidadeDescrita = modelo.QuantidadeDescrita,
            Justificativa = modelo.Justificativa,
        });

        TempData["MensagemSucesso"] = $"{(tipo == TipoOperacaoAmostra.CONSUMO ? "Consumo" : "Exaurimento")} registrado para o vestígio {vestigio.RotuloEvidencia}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// A origem do registro no ledger é a própria OPERACAO_AMOSTRA (não o
    /// vestígio): um mesmo vestígio pode ser fracionado, unificado,
    /// consumido ou exaurido mais de uma vez ao longo da perícia, e a
    /// unicidade (entidade_origem, registro_origem_id, evento) do
    /// REGISTRO_LEDGER rejeitaria a segunda ocorrência se a origem fosse
    /// o vestígio em si.
    /// </summary>
    private async Task RegistrarEventoOperacaoAsync(long operacaoAmostraId, long vestigioId, string evento, long executorId, object payloadDados)
    {
        var agora = DateTime.UtcNow;
        var executor = await db.Intervenientes.FindAsync(executorId);
        var payloadJson = JsonSerializer.Serialize(payloadDados);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigioId.ToString(), evento, executor!.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = executorId,
            EmissorId = executorId,
            VestigioId = vestigioId,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "OPERACAO_AMOSTRA",
            RegistroOrigemId = operacaoAmostraId,
            VestigioId = vestigioId,
            Evento = evento,
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();
    }

    private bool TryObterIntervenienteId(out long intervenienteId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out intervenienteId) && intervenienteId > 0;
}
