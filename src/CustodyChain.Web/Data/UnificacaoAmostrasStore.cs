using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class UnificacaoAmostrasStore(CustodyChainDbContext db) : IUnificacaoAmostrasStore
{
    public Task<ContextoUnificacaoAmostras?> ObterContextoAsync(long periciaId, long peritoId, CancellationToken cancellationToken) =>
        (from pericia in db.Pericias join perito in db.Intervenientes on peritoId equals perito.Id
         where pericia.Id == periciaId && pericia.PeritoId == peritoId && pericia.Situacao == SituacaoPericia.EM_EXECUCAO
               && perito.Situacao == SituacaoInterveniente.ATIVO
         select new ContextoUnificacaoAmostras(pericia.Id, pericia.VestigioId, perito.Did)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<OrigemUnificacaoAmostra>> ObterOrigensAsync(IReadOnlyList<long> vestigioIds, CancellationToken cancellationToken) =>
        await db.Vestigios.Where(v => vestigioIds.Contains(v.Id)).Select(v => new OrigemUnificacaoAmostra(
            v.Id, v.RotuloEvidencia, v.RotuloConjunto, v.ProcessoId, v.TipoVestigioId, v.HashSha256)).ToListAsync(cancellationToken);

    public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) =>
        db.Vestigios.AnyAsync(v => v.RotuloEvidencia == rotuloEvidencia, cancellationToken);

    public async Task PersistirAsync(UnificacaoAmostrasPendente unificacao, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await db.Pericias.SingleOrDefaultAsync(p => p.Id == unificacao.PericiaId && p.PeritoId == unificacao.PeritoId && p.Situacao == SituacaoPericia.EM_EXECUCAO, cancellationToken);
            var peritoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == unificacao.PeritoId && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);
            var ids = unificacao.Origens.Select(o => o.VestigioId).ToList();
            var origens = await ObterOrigensAsync(ids, cancellationToken);
            var rotuloEmUso = await RotuloEvidenciaExisteAsync(unificacao.RotuloEvidenciaResultante, cancellationToken);
            if (pericia is null || !peritoAtivo || rotuloEmUso || origens.Count != ids.Count || origens.Select(o => o.RotuloConjunto).Distinct().Count() != 1)
                throw new ConflitoUnificacaoAmostrasException("A perícia, as origens ou o rótulo mudou enquanto a unificação era preparada. Atualize a página e tente novamente.");

            var resultante = new Vestigio
            {
                RotuloEvidencia = unificacao.RotuloEvidenciaResultante, RotuloConjunto = origens[0].RotuloConjunto,
                ProcessoId = origens[0].ProcessoId, TipoVestigioId = origens[0].TipoVestigioId, Descricao = unificacao.DescricaoResultante,
                CriadorId = unificacao.PeritoId, CustodianteAtualId = unificacao.PeritoId, HashSha256 = unificacao.HashCombinado,
                EtapaAtual = 8, FaseAtual = FaseVestigio.INTERNA, Estado = EstadoVestigio.EmPericia, CriadoEm = unificacao.ExecutadoEm,
            };
            db.Vestigios.Add(resultante);
            await db.SaveChangesAsync(cancellationToken);
            var operacoes = origens.Select(origem => new OperacaoAmostra
            {
                PericiaId = unificacao.PericiaId, Tipo = TipoOperacaoAmostra.UNIFICACAO, VestigioOrigemId = origem.VestigioId,
                VestigioResultanteId = resultante.Id, Justificativa = unificacao.Justificativa, ExecutadoPorId = unificacao.PeritoId, ExecutadoEm = unificacao.ExecutadoEm,
            }).ToList();
            db.OperacoesAmostra.AddRange(operacoes);
            await db.SaveChangesAsync(cancellationToken);
            db.Credenciais.Add(new Credencial { Tipo = TipoCredencial.COC, Identificador = unificacao.CredencialId, TitularId = unificacao.PeritoId, EmissorId = unificacao.PeritoId, VestigioId = resultante.Id, EmitidaEm = unificacao.ExecutadoEm, Situacao = SituacaoCredencial.PENDENTE });
            db.RegistrosLedger.Add(new RegistroLedger { EntidadeOrigem = "OPERACAO_AMOSTRA", RegistroOrigemId = operacoes[0].Id, VestigioId = resultante.Id, Evento = "UNIFICACAO", PayloadJson = unificacao.PayloadJson, PayloadHashSha256 = unificacao.PayloadHashSha256, CredencialId = unificacao.CredencialId, DidResponsavel = unificacao.DidResponsavel, ChaveIdempotencia = unificacao.CredencialId, Estado = EstadoRegistroLedger.PENDENTE, Tentativas = 0, CriadoEm = unificacao.ExecutadoEm, ProximaTentativaEm = unificacao.ExecutadoEm });
            db.LogsAuditoria.Add(CriarLog(operacoes[0].Id, unificacao));
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoUnificacaoAmostrasException("Não foi possível concluir a unificação porque um vestígio, credencial ou evento já existe.");
        }
    }

    private LogAuditoria CriarLog(long operacaoId, UnificacaoAmostrasPendente unificacao)
    {
        var anterior = db.LogsAuditoria.OrderByDescending(l => l.Id).Select(l => l.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', anterior, "UNIFICACAO", "OPERACAO_AMOSTRA", operacaoId, unificacao.PeritoId, unificacao.ExecutadoEm.Ticks);
        return new LogAuditoria { IntervenienteId = unificacao.PeritoId, Acao = "UNIFICACAO", Entidade = "OPERACAO_AMOSTRA", RegistroId = operacaoId, DataHora = unificacao.ExecutadoEm, HashAnterior = anterior, HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))) };
    }
}
