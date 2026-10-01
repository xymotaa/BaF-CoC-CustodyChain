using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class FracionamentoAmostraStore(CustodyChainDbContext db) : IFracionamentoAmostraStore
{
    public Task<ContextoFracionamentoAmostra?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        CancellationToken cancellationToken) =>
        (from pericia in db.Pericias
         join perito in db.Intervenientes on peritoId equals perito.Id
         where pericia.Id == periciaId
             && pericia.PeritoId == peritoId
             && pericia.Situacao == SituacaoPericia.EM_EXECUCAO
             && perito.Situacao == SituacaoInterveniente.ATIVO
         select new ContextoFracionamentoAmostra(
             pericia.Id,
             pericia.VestigioId,
             pericia.Vestigio.RotuloEvidencia,
             pericia.Vestigio.RotuloConjunto,
             pericia.Vestigio.ProcessoId,
             pericia.Vestigio.TipoVestigioId,
             pericia.Vestigio.HashSha256,
             perito.Did))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) =>
        db.Vestigios.AnyAsync(v => v.RotuloEvidencia == rotuloEvidencia, cancellationToken);

    public async Task PersistirAsync(FracionamentoAmostraPendente fracionamento, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await db.Pericias
                .Include(p => p.Vestigio)
                .SingleOrDefaultAsync(p => p.Id == fracionamento.PericiaId
                    && p.PeritoId == fracionamento.PeritoId
                    && p.Situacao == SituacaoPericia.EM_EXECUCAO, cancellationToken);
            var peritoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == fracionamento.PeritoId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);
            var rotuloEmUso = await db.Vestigios.AnyAsync(v => v.RotuloEvidencia == fracionamento.RotuloEvidenciaResultante, cancellationToken);

            if (pericia is null || !peritoAtivo || rotuloEmUso)
                throw new ConflitoFracionamentoAmostraException(
                    "A perícia, o responsável ou o rótulo do vestígio mudou enquanto o fracionamento era preparado. Atualize a página e tente novamente.");

            var origem = pericia.Vestigio;
            var resultante = new Vestigio
            {
                RotuloEvidencia = fracionamento.RotuloEvidenciaResultante,
                RotuloConjunto = origem.RotuloConjunto,
                ProcessoId = origem.ProcessoId,
                TipoVestigioId = origem.TipoVestigioId,
                Descricao = fracionamento.DescricaoResultante,
                CriadorId = fracionamento.PeritoId,
                CustodianteAtualId = fracionamento.PeritoId,
                HashSha256 = origem.HashSha256,
                EtapaAtual = 8,
                FaseAtual = FaseVestigio.INTERNA,
                Estado = EstadoVestigio.EmPericia,
                CriadoEm = fracionamento.ExecutadoEm,
            };
            db.Vestigios.Add(resultante);
            await db.SaveChangesAsync(cancellationToken);

            var operacao = new OperacaoAmostra
            {
                PericiaId = pericia.Id,
                Tipo = TipoOperacaoAmostra.FRACIONAMENTO,
                VestigioOrigemId = origem.Id,
                VestigioResultanteId = resultante.Id,
                QuantidadeDescrita = fracionamento.QuantidadeDescrita,
                Justificativa = fracionamento.Justificativa,
                ExecutadoPorId = fracionamento.PeritoId,
                ExecutadoEm = fracionamento.ExecutadoEm,
            };
            db.OperacoesAmostra.Add(operacao);
            await db.SaveChangesAsync(cancellationToken);

            db.Credenciais.Add(new Credencial
            {
                Tipo = TipoCredencial.COC,
                Identificador = fracionamento.CredencialId,
                TitularId = fracionamento.PeritoId,
                EmissorId = fracionamento.PeritoId,
                VestigioId = origem.Id,
                EmitidaEm = fracionamento.ExecutadoEm,
                Situacao = SituacaoCredencial.PENDENTE,
            });
            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "OPERACAO_AMOSTRA",
                RegistroOrigemId = operacao.Id,
                VestigioId = origem.Id,
                Evento = "FRACIONAMENTO",
                PayloadJson = fracionamento.PayloadJson,
                PayloadHashSha256 = fracionamento.PayloadHashSha256,
                CredencialId = fracionamento.CredencialId,
                DidResponsavel = fracionamento.DidResponsavel,
                ChaveIdempotencia = fracionamento.CredencialId,
                Estado = EstadoRegistroLedger.PENDENTE,
                Tentativas = 0,
                CriadoEm = fracionamento.ExecutadoEm,
                ProximaTentativaEm = fracionamento.ExecutadoEm,
            });
            db.LogsAuditoria.Add(CriarLogFracionamento(operacao.Id, fracionamento));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoFracionamentoAmostraException(
                "Não foi possível concluir o fracionamento porque o vestígio, a credencial ou o evento já existe.");
        }
    }

    private LogAuditoria CriarLogFracionamento(long operacaoId, FracionamentoAmostraPendente fracionamento)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "FRACIONAMENTO", "OPERACAO_AMOSTRA", operacaoId,
            fracionamento.PeritoId, fracionamento.ExecutadoEm.Ticks);

        return new LogAuditoria
        {
            IntervenienteId = fracionamento.PeritoId,
            Acao = "FRACIONAMENTO",
            Entidade = "OPERACAO_AMOSTRA",
            RegistroId = operacaoId,
            DataHora = fracionamento.ExecutadoEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
