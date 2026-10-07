using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RecebimentoPericiaStore(CustodyChainDbContext db) : IRecebimentoPericiaStore
{
    public Task<ContextoRecebimentoPericia?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken) =>
        (from pericia in db.Pericias
         join perito in db.Intervenientes on peritoId equals perito.Id
         where pericia.Id == periciaId
             && pericia.PeritoId == peritoId
             && pericia.Situacao == SituacaoPericia.DESIGNADA
             && perito.Situacao == SituacaoInterveniente.ATIVO
             && pericia.Credencial != null
             && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
             && (pericia.Credencial.ValidaAte == null || pericia.Credencial.ValidaAte > agora)
         select new ContextoRecebimentoPericia(
             pericia.Id,
             pericia.VestigioId,
             pericia.ProcessoId,
             pericia.Vestigio.RotuloEvidencia,
             perito.Did,
             pericia.Credencial!.Identificador))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task PersistirAsync(RecebimentoPericiaConfirmado recebimento, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await db.Pericias
                .Include(p => p.Vestigio)
                .Include(p => p.Credencial)
                .SingleOrDefaultAsync(p => p.Id == recebimento.PericiaId
                    && p.PeritoId == recebimento.PeritoId
                    && p.Situacao == SituacaoPericia.DESIGNADA, cancellationToken);
            var peritoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == recebimento.PeritoId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);

            if (pericia is null || !peritoAtivo || !PossuiCredencialValida(pericia, recebimento.RecebidoEm))
                throw new ConflitoRecebimentoPericiaException(
                    "A perícia, o responsável ou a permissão mudou enquanto o recebimento era confirmado. Atualize a página e tente novamente.");

            pericia.Situacao = SituacaoPericia.RECEBIDA;
            pericia.RecebidaEm = recebimento.RecebidoEm;
            pericia.Vestigio.CustodianteAtualId = recebimento.PeritoId;
            pericia.Vestigio.AtualizadoEm = recebimento.RecebidoEm;

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "PERICIA",
                RegistroOrigemId = pericia.Id,
                VestigioId = pericia.VestigioId,
                Evento = "PERICIA_RECEBER",
                PayloadJson = recebimento.OperacaoAssinadaJson,
                PayloadHashSha256 = recebimento.OperacaoAssinadaHashSha256,
                DidResponsavel = recebimento.DidResponsavel,
                ChaveIdempotencia = recebimento.OperacaoAssinadaId,
                OperacaoAssinadaId = recebimento.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = recebimento.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = recebimento.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = recebimento.RecebidoEm,
                AncoradoEm = recebimento.ConfirmadoEm,
            });

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRecebimentoPericiaException(
                "Não foi possível concluir o recebimento porque a perícia ou o registro de autorização já existe.");
        }
    }

    private static bool PossuiCredencialValida(Pericia pericia, DateTime agora) =>
        pericia.Credencial is not null
        && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
        && pericia.Credencial.VestigioId == pericia.VestigioId
        && (pericia.Credencial.ValidaAte is null || pericia.Credencial.ValidaAte > agora);
}
