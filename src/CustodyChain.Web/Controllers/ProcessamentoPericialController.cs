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
public class ProcessamentoPericialController(CustodyChainDbContext db, IServicoLedger ledger) : Controller
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
    public async Task<IActionResult> RomperLacre(RomperLacreViewModel modelo)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .Include(p => p.Credencial)
            .FirstOrDefaultAsync(p => p.Id == modelo.PericiaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.RECEBIDA);

        if (pericia is null || string.IsNullOrWhiteSpace(modelo.Justificativa))
        {
            TempData["MensagemErro"] = "Perícia não encontrada, vestígio ainda não recebido, ou justificativa não informada.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;

        // RN10: só executa com credencial de permissão vigente para aquele
        // vestígio específico. Checagem repetida aqui (não só na listagem)
        // porque a situação da credencial pode ter mudado entre o GET e o
        // POST (ex: revogada nesse intervalo).
        var credencialValida = pericia.Credencial is not null
            && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
            && pericia.Credencial.VestigioId == pericia.VestigioId
            && (pericia.Credencial.ValidaAte is null || pericia.Credencial.ValidaAte > agora);

        if (!credencialValida)
        {
            TempData["MensagemErro"] = "Credencial de permissão inválida, revogada ou expirada para este vestígio (RN10).";
            return RedirectToAction(nameof(Index));
        }

        var lacreAtual = await db.Lacres
            .Where(l => l.VestigioId == pericia.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
            .OrderByDescending(l => l.AplicadoEm)
            .FirstOrDefaultAsync();

        if (lacreAtual is null)
        {
            TempData["MensagemErro"] = "Nenhum lacre intacto encontrado para este vestígio.";
            return RedirectToAction(nameof(Index));
        }

        lacreAtual.Situacao = SituacaoLacre.ROMPIDO;
        lacreAtual.RompidoPorId = peritoId;
        lacreAtual.RompidoEm = agora;
        lacreAtual.JustificativaRompimento = modelo.Justificativa.Trim();

        pericia.Situacao = SituacaoPericia.EM_EXECUCAO;
        pericia.Vestigio.Estado = EstadoVestigio.EmPericia;
        pericia.Vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        var perito = await db.Intervenientes.FindAsync(peritoId);
        var payload = new
        {
            RE = pericia.Vestigio.RotuloEvidencia,
            NumeroLacreRompido = lacreAtual.Numero,
            Justificativa = lacreAtual.JustificativaRompimento,
            RompidoPor = perito!.Did,
            RompidoEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        // Credencial #4 (Quadro 14): rompimento.
        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(pericia.VestigioId.ToString(), "ROMPIMENTO", perito.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = peritoId,
            EmissorId = peritoId,
            VestigioId = pericia.VestigioId,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "LACRE",
            RegistroOrigemId = lacreAtual.Id,
            VestigioId = pericia.VestigioId,
            Evento = "ROMPIMENTO",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Lacre {lacreAtual.Numero} rompido. Vestígio {pericia.Vestigio.RotuloEvidencia} liberado para exame.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/emitir-laudo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmitirLaudo(EmitirLaudoViewModel modelo)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // RN08: só peritos emitem laudos — garantido aqui porque a ação só
        // é alcançável por uma perícia designada ao perito autenticado.
        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .FirstOrDefaultAsync(p => p.Id == modelo.PericiaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.EM_EXECUCAO);

        if (pericia is null || string.IsNullOrWhiteSpace(modelo.Conteudo))
        {
            TempData["MensagemErro"] = "Perícia não encontrada, lacre ainda não rompido, ou conteúdo do laudo não informado.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        var vestigio = pericia.Vestigio;

        if (string.IsNullOrEmpty(vestigio.HashSha256))
        {
            TempData["MensagemErro"] = "Vestígio sem hash SHA-256 registrado (RN13) — não é possível vincular o laudo.";
            return RedirectToAction(nameof(Index));
        }

        // RN13: o laudo se vincula ao hash dos vestígios que fundamentaram
        // a análise. Esta perícia cobre um único vestígio, então o vínculo
        // é o próprio hash gravado na coleta (T-03).
        var hashVestigios = vestigio.HashSha256;

        var conteudo = modelo.Conteudo.Trim();
        var hashLaudo = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo + hashVestigios)));

        var numero = $"LAUDO-{agora:yyyy}-{pericia.Id:D6}";

        var laudo = new Laudo
        {
            PericiaId = pericia.Id,
            Numero = numero,
            Versao = 1,
            Conteudo = conteudo,
            HashVestigios = hashVestigios,
            HashLaudo = hashLaudo,
            // Assinatura Ed25519 real depende da wallet do titular, ainda
            // não implementada (pendência P-02 do documento de contexto,
            // classificada como evolução do MVP). Fica nula por ora.
            AssinaturaEd25519 = null,
            AssinadoPorId = peritoId,
            AssinadoEm = agora,
        };
        db.Laudos.Add(laudo);

        pericia.Situacao = SituacaoPericia.CONCLUIDA;
        pericia.ConcluidaEm = agora;
        vestigio.Estado = EstadoVestigio.Periciado;
        vestigio.AtualizadoEm = agora;

        await db.SaveChangesAsync();

        var perito = await db.Intervenientes.FindAsync(peritoId);
        var payload = new
        {
            RE = vestigio.RotuloEvidencia,
            NumeroLaudo = laudo.Numero,
            Versao = laudo.Versao,
            HashVestigios = hashVestigios,
            HashLaudo = hashLaudo,
            EmitidoPor = perito!.Did,
            EmitidoEm = agora,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var hashPayload = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

        // Credencial #5 (Quadro 14): laudo.
        var credencialId = await ledger.EmitirCredencialCoCAsync(
            new CredencialCoCDto(vestigio.Id.ToString(), "LAUDO", perito.Did, hashPayload));

        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = peritoId,
            EmissorId = peritoId,
            VestigioId = vestigio.Id,
            EmitidaEm = agora,
            Situacao = SituacaoCredencial.VIGENTE,
        });

        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "LAUDO",
            RegistroOrigemId = laudo.Id,
            VestigioId = vestigio.Id,
            Evento = "LAUDO",
            PayloadJson = payloadJson,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = agora,
        });

        await db.SaveChangesAsync();

        TempData["MensagemSucesso"] = $"Laudo {laudo.Numero} emitido para o vestígio {vestigio.RotuloEvidencia}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/processamento-pericial/fracionar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fracionar(FracionarViewModel modelo)
    {
        var peritoId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var pericia = await db.Pericias
            .Include(p => p.Vestigio)
            .FirstOrDefaultAsync(p => p.Id == modelo.PericiaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.EM_EXECUCAO);

        if (pericia is null
            || string.IsNullOrWhiteSpace(modelo.RotuloEvidenciaResultante)
            || string.IsNullOrWhiteSpace(modelo.DescricaoResultante)
            || string.IsNullOrWhiteSpace(modelo.Justificativa))
        {
            TempData["MensagemErro"] = "Perícia não encontrada, lacre ainda não rompido, ou campos obrigatórios não informados.";
            return RedirectToAction(nameof(Index));
        }

        if (await db.Vestigios.AnyAsync(v => v.RotuloEvidencia == modelo.RotuloEvidenciaResultante))
        {
            TempData["MensagemErro"] = "Já existe um vestígio com este rótulo de evidência.";
            return RedirectToAction(nameof(Index));
        }

        var agora = DateTime.UtcNow;
        var origem = pericia.Vestigio;

        // RN18: o fracionamento preserva o rótulo de conjunto de origem —
        // o item resultante entra sob o mesmo RC, com um RE próprio.
        var resultante = new Vestigio
        {
            RotuloEvidencia = modelo.RotuloEvidenciaResultante.Trim(),
            RotuloConjunto = origem.RotuloConjunto,
            ProcessoId = origem.ProcessoId,
            TipoVestigioId = origem.TipoVestigioId,
            Descricao = modelo.DescricaoResultante.Trim(),
            CriadorId = peritoId,
            CustodianteAtualId = peritoId,
            HashSha256 = origem.HashSha256,
            EtapaAtual = 8,
            FaseAtual = FaseVestigio.INTERNA,
            Estado = EstadoVestigio.EmPericia,
            CriadoEm = agora,
        };
        db.Vestigios.Add(resultante);
        await db.SaveChangesAsync();

        var operacao = new OperacaoAmostra
        {
            PericiaId = pericia.Id,
            Tipo = TipoOperacaoAmostra.FRACIONAMENTO,
            VestigioOrigemId = origem.Id,
            VestigioResultanteId = resultante.Id,
            QuantidadeDescrita = modelo.QuantidadeDescrita,
            Justificativa = modelo.Justificativa.Trim(),
            ExecutadoPorId = peritoId,
            ExecutadoEm = agora,
        };
        db.OperacoesAmostra.Add(operacao);

        await db.SaveChangesAsync();

        await RegistrarEventoOperacaoAsync(operacao.Id, origem.Id, "FRACIONAMENTO", peritoId, new
        {
            REOrigem = origem.RotuloEvidencia,
            REResultante = resultante.RotuloEvidencia,
            RC = origem.RotuloConjunto,
            Justificativa = modelo.Justificativa,
        });

        TempData["MensagemSucesso"] = $"Vestígio {origem.RotuloEvidencia} fracionado. Novo item: {resultante.RotuloEvidencia}.";
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
}
