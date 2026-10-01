using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RecebimentoStore(CustodyChainDbContext db) : IRecebimentoStore
{
    public async Task<ContextoRecebimento?> ObterContextoAsync(
        long movimentacaoId,
        long destinoId,
        CancellationToken cancellationToken)
    {
        var contexto = await (from movimentacao in db.Movimentacoes
                              join destino in db.Intervenientes on movimentacao.DestinoId equals destino.Id
                              where movimentacao.Id == movimentacaoId
                                  && movimentacao.Situacao == SituacaoMovimentacao.PENDENTE
                                  && movimentacao.DestinoId == destinoId
                                  && destino.Situacao == SituacaoInterveniente.ATIVO
                              select new
                              {
                                  movimentacao.Id,
                                  movimentacao.VestigioId,
                                  movimentacao.Vestigio.RotuloEvidencia,
                                  destino.Did,
                              }).SingleOrDefaultAsync(cancellationToken);

        if (contexto is null)
            return null;

        var lacreEsperado = await db.Lacres
            .Where(l => l.VestigioId == contexto.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
            .OrderByDescending(l => l.AplicadoEm)
            .Select(l => l.Numero)
            .FirstOrDefaultAsync(cancellationToken);

        return new ContextoRecebimento(
            contexto.Id,
            contexto.VestigioId,
            contexto.RotuloEvidencia,
            contexto.Did,
            lacreEsperado);
    }

    public async Task ConfirmarAsync(RecebimentoConfirmadoPendente recebimento, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var movimentacao = await db.Movimentacoes
                .Include(m => m.Vestigio)
                .SingleOrDefaultAsync(m => m.Id == recebimento.MovimentacaoId
                    && m.DestinoId == recebimento.DestinoId
                    && m.Situacao == SituacaoMovimentacao.PENDENTE, cancellationToken);
            var destinoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == recebimento.DestinoId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);

            if (movimentacao is null || !destinoAtivo)
                throw new ConflitoRecebimentoException("O recebimento mudou enquanto era confirmado. Atualize a página e tente novamente.");

            var lacreEsperadoAtual = await ObterNumeroLacreEsperadoAsync(movimentacao.VestigioId, cancellationToken);
            if (lacreEsperadoAtual != recebimento.NumeroLacreEsperado)
                throw new ConflitoRecebimentoException("O lacre esperado mudou enquanto era conferido. Atualize a página e tente novamente.");

            movimentacao.DataHoraChegada = recebimento.RecebidoEm;
            movimentacao.CondicoesAdequadas = recebimento.LacreConfere;
            movimentacao.AprovadoPorId = recebimento.DestinoId;
            movimentacao.Situacao = SituacaoMovimentacao.ACEITA;

            var vestigio = movimentacao.Vestigio;
            vestigio.Estado = recebimento.LacreConfere
                ? EstadoVestigio.Recebido
                : EstadoVestigio.CustodiaComprometida;
            vestigio.EtapaAtual = 7;
            vestigio.CustodianteAtualId = recebimento.DestinoId;
            vestigio.AtualizadoEm = recebimento.RecebidoEm;

            AdicionarEventoPendente(movimentacao, vestigio.Id, recebimento.Evento, recebimento.RecebidoEm,
                recebimento.PayloadJson, recebimento.PayloadHashSha256, recebimento.CredencialId,
                recebimento.DidResponsavel, recebimento.DestinoId);
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRecebimentoException("Não foi possível concluir o recebimento porque uma credencial ou evento já existe.");
        }
    }

    public async Task RecusarAsync(RecebimentoRecusadoPendente recebimento, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var movimentacao = await db.Movimentacoes
                .Include(m => m.Vestigio)
                .SingleOrDefaultAsync(m => m.Id == recebimento.MovimentacaoId
                    && m.DestinoId == recebimento.DestinoId
                    && m.Situacao == SituacaoMovimentacao.PENDENTE, cancellationToken);
            var destinoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == recebimento.DestinoId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);

            if (movimentacao is null || !destinoAtivo || movimentacao.OrigemId is null)
                throw new ConflitoRecebimentoException("O recebimento mudou enquanto era recusado. Atualize a página e tente novamente.");

            movimentacao.Situacao = SituacaoMovimentacao.RECUSADA;
            movimentacao.MotivoRecusa = recebimento.MotivoRecusa;
            movimentacao.DataHoraChegada = recebimento.RecusadoEm;
            movimentacao.AprovadoPorId = recebimento.DestinoId;

            var vestigio = movimentacao.Vestigio;
            vestigio.Estado = EstadoVestigio.Coletado;
            vestigio.CustodianteAtualId = movimentacao.OrigemId;
            vestigio.AtualizadoEm = recebimento.RecusadoEm;

            AdicionarEventoPendente(movimentacao, vestigio.Id, "RECUSA", recebimento.RecusadoEm,
                recebimento.PayloadJson, recebimento.PayloadHashSha256, recebimento.CredencialId,
                recebimento.DidResponsavel, recebimento.DestinoId);
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRecebimentoException("Não foi possível recusar o recebimento porque uma credencial ou evento já existe.");
        }
    }

    private async Task<string?> ObterNumeroLacreEsperadoAsync(long vestigioId, CancellationToken cancellationToken) =>
        await db.Lacres
            .Where(l => l.VestigioId == vestigioId && l.Situacao == SituacaoLacre.INTACTO)
            .OrderByDescending(l => l.AplicadoEm)
            .Select(l => l.Numero)
            .FirstOrDefaultAsync(cancellationToken);

    private void AdicionarEventoPendente(
        Movimentacao movimentacao,
        long vestigioId,
        string evento,
        DateTime ocorridoEm,
        string payloadJson,
        string payloadHashSha256,
        string credencialId,
        string didResponsavel,
        long responsavelId)
    {
        db.Credenciais.Add(new Credencial
        {
            Tipo = TipoCredencial.COC,
            Identificador = credencialId,
            TitularId = responsavelId,
            EmissorId = responsavelId,
            VestigioId = vestigioId,
            EmitidaEm = ocorridoEm,
            Situacao = SituacaoCredencial.PENDENTE,
        });
        db.RegistrosLedger.Add(new RegistroLedger
        {
            EntidadeOrigem = "MOVIMENTACAO",
            RegistroOrigemId = movimentacao.Id,
            VestigioId = vestigioId,
            Evento = evento,
            PayloadJson = payloadJson,
            PayloadHashSha256 = payloadHashSha256,
            CredencialId = credencialId,
            DidResponsavel = didResponsavel,
            ChaveIdempotencia = credencialId,
            Estado = EstadoRegistroLedger.PENDENTE,
            Tentativas = 0,
            CriadoEm = ocorridoEm,
            ProximaTentativaEm = ocorridoEm,
        });
        db.LogsAuditoria.Add(CriarLog(movimentacao.Id, evento, responsavelId, ocorridoEm));
    }

    private LogAuditoria CriarLog(long movimentacaoId, string acao, long responsavelId, DateTime ocorridoEm)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, acao, "MOVIMENTACAO", movimentacaoId, responsavelId, ocorridoEm.Ticks);
        return new LogAuditoria
        {
            IntervenienteId = responsavelId,
            Acao = acao,
            Entidade = "MOVIMENTACAO",
            RegistroId = movimentacaoId,
            DataHora = ocorridoEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
