using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class UnificacaoAmostrasStore(CustodyChainDbContext db) : IUnificacaoAmostrasStore
{
    public Task<ContextoUnificacaoAmostras?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken) =>
        (from pericia in db.Pericias
         join perito in db.Intervenientes.AptosParaOperacoesLedger() on peritoId equals perito.Id
         where pericia.Id == periciaId
             && pericia.PeritoId == peritoId
             && pericia.Situacao == SituacaoPericia.EM_EXECUCAO
             && perito.Situacao == SituacaoInterveniente.ATIVO
             && pericia.Credencial != null
             && pericia.Credencial.Tipo == TipoCredencial.PERMISSAO
             && pericia.Credencial.VestigioId == pericia.VestigioId
             && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
             && (pericia.Credencial.ValidaAte == null || pericia.Credencial.ValidaAte > agora)
         select new ContextoUnificacaoAmostras(pericia.Id, pericia.VestigioId, perito.Did))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<OrigemUnificacaoAmostra>> ObterOrigensAsync(
        IReadOnlyList<long> vestigioIds,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken) =>
        await (from vestigio in db.Vestigios
               join credencial in db.Credenciais on vestigio.Id equals credencial.VestigioId
               where vestigioIds.Contains(vestigio.Id)
                   && credencial.TitularId == peritoId
                   && credencial.Tipo == TipoCredencial.PERMISSAO
                   && credencial.Situacao == SituacaoCredencial.VIGENTE
                   && credencial.ProcessoId == vestigio.ProcessoId
                   && (credencial.ValidaAte == null || credencial.ValidaAte > agora)
               select new OrigemUnificacaoAmostra(
                   vestigio.Id,
                   vestigio.RotuloEvidencia,
                   vestigio.RotuloConjunto,
                   vestigio.ProcessoId,
                   vestigio.TipoVestigioId,
                   vestigio.HashSha256,
                   credencial.Identificador))
            .OrderBy(origem => origem.VestigioId)
            .ToListAsync(cancellationToken);

    public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) =>
        db.Vestigios.AnyAsync(vestigio => vestigio.RotuloEvidencia == rotuloEvidencia, cancellationToken);

    public async Task PersistirAsync(UnificacaoAmostrasPendente unificacao, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await db.Pericias
                .Include(item => item.Credencial)
                .SingleOrDefaultAsync(item => item.Id == unificacao.PericiaId
                    && item.PeritoId == unificacao.PeritoId
                    && item.Situacao == SituacaoPericia.EM_EXECUCAO, cancellationToken);
            var peritoAtivo = await db.Intervenientes.AptosParaOperacoesLedger()
                .AnyAsync(item => item.Id == unificacao.PeritoId, cancellationToken);
            var ids = unificacao.Origens.Select(origem => origem.VestigioId).ToList();
            var origensAtuais = await ObterOrigensAsync(ids, unificacao.PeritoId, unificacao.ExecutadoEm, cancellationToken);
            var rotuloEmUso = await RotuloEvidenciaExisteAsync(unificacao.RotuloEvidenciaResultante, cancellationToken);

            if (pericia is null || !peritoAtivo || !PossuiCredencialValida(pericia, unificacao.ExecutadoEm)
                || rotuloEmUso || !OrigensSaoIguais(unificacao.Origens, origensAtuais)
                || !PossuemMesmoConjuntoEProcesso(origensAtuais))
                throw new ConflitoUnificacaoAmostrasException(
                    "A perícia, as permissões das origens ou o rótulo mudou enquanto a unificação era preparada. Atualize a página e tente novamente.");

            var resultante = new Vestigio
            {
                RotuloEvidencia = unificacao.RotuloEvidenciaResultante,
                RotuloConjunto = origensAtuais[0].RotuloConjunto,
                ProcessoId = origensAtuais[0].ProcessoId,
                TipoVestigioId = origensAtuais[0].TipoVestigioId,
                Descricao = unificacao.DescricaoResultante,
                CriadorId = unificacao.PeritoId,
                CustodianteAtualId = unificacao.PeritoId,
                HashSha256 = unificacao.HashCombinado,
                EtapaAtual = 8,
                FaseAtual = FaseVestigio.INTERNA,
                Estado = EstadoVestigio.EmPericia,
                CriadoEm = unificacao.ExecutadoEm,
            };
            db.Vestigios.Add(resultante);
            await db.SaveChangesAsync(cancellationToken);

            var operacoes = origensAtuais.Select(origem => new OperacaoAmostra
            {
                PericiaId = unificacao.PericiaId,
                Tipo = TipoOperacaoAmostra.UNIFICACAO,
                VestigioOrigemId = origem.VestigioId,
                VestigioResultanteId = resultante.Id,
                Justificativa = unificacao.Justificativa,
                ExecutadoPorId = unificacao.PeritoId,
                ExecutadoEm = unificacao.ExecutadoEm,
            }).ToList();
            db.OperacoesAmostra.AddRange(operacoes);
            await db.SaveChangesAsync(cancellationToken);

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "OPERACAO_AMOSTRA",
                RegistroOrigemId = operacoes[0].Id,
                VestigioId = resultante.Id,
                Evento = "AMOSTRA_UNIFICAR",
                PayloadJson = unificacao.OperacaoAssinadaJson,
                PayloadHashSha256 = unificacao.OperacaoAssinadaHashSha256,
                DidResponsavel = unificacao.DidResponsavel,
                ChaveIdempotencia = unificacao.OperacaoAssinadaId,
                OperacaoAssinadaId = unificacao.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = unificacao.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = unificacao.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = unificacao.ExecutadoEm,
                AncoradoEm = unificacao.ConfirmadoEm,
            });
            db.LogsAuditoria.Add(CriarLog(operacoes[0].Id, unificacao));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoUnificacaoAmostrasException(
                "Não foi possível concluir a unificação porque um vestígio, permissão ou evento já existe.");
        }
    }

    private static bool PossuiCredencialValida(Pericia pericia, DateTime agora) =>
        pericia.Credencial is not null
        && pericia.Credencial.Tipo == TipoCredencial.PERMISSAO
        && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
        && pericia.Credencial.VestigioId == pericia.VestigioId
        && (pericia.Credencial.ValidaAte is null || pericia.Credencial.ValidaAte > agora);

    private static bool OrigensSaoIguais(
        IReadOnlyList<OrigemUnificacaoAmostra> esperadas,
        IReadOnlyList<OrigemUnificacaoAmostra> atuais) =>
        esperadas.Count == atuais.Count
        && esperadas.OrderBy(item => item.VestigioId).Zip(atuais.OrderBy(item => item.VestigioId))
            .All(par => par.First == par.Second);

    private static bool PossuemMesmoConjuntoEProcesso(IReadOnlyList<OrigemUnificacaoAmostra> origens) =>
        origens.Count > 1
        && origens.Select(origem => origem.RotuloConjunto).Distinct(StringComparer.Ordinal).Count() == 1
        && origens.Select(origem => origem.ProcessoId).Distinct().Count() == 1;

    private LogAuditoria CriarLog(long operacaoId, UnificacaoAmostrasPendente unificacao)
    {
        var anterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', anterior, "UNIFICACAO", "OPERACAO_AMOSTRA", operacaoId,
            unificacao.PeritoId, unificacao.ExecutadoEm.Ticks);

        return new LogAuditoria
        {
            IntervenienteId = unificacao.PeritoId,
            Acao = "UNIFICACAO",
            Entidade = "OPERACAO_AMOSTRA",
            RegistroId = operacaoId,
            DataHora = unificacao.ExecutadoEm,
            HashAnterior = anterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
