using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class EmissaoLaudoStore(CustodyChainDbContext db) : IEmissaoLaudoStore
{
    public Task<ContextoEmissaoLaudo?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        CancellationToken cancellationToken) =>
        (from pericia in db.Pericias
         join perito in db.Intervenientes on peritoId equals perito.Id
         where pericia.Id == periciaId
             && pericia.PeritoId == peritoId
             && pericia.Situacao == SituacaoPericia.EM_EXECUCAO
             && perito.Situacao == SituacaoInterveniente.ATIVO
         select new ContextoEmissaoLaudo(
             pericia.Id,
             pericia.VestigioId,
             pericia.Vestigio.RotuloEvidencia,
             perito.Did,
             pericia.Vestigio.HashSha256))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task PersistirAsync(LaudoPendente laudoPendente, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await db.Pericias
                .Include(p => p.Vestigio)
                .SingleOrDefaultAsync(p => p.Id == laudoPendente.PericiaId
                    && p.PeritoId == laudoPendente.PeritoId
                    && p.Situacao == SituacaoPericia.EM_EXECUCAO, cancellationToken);
            var peritoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == laudoPendente.PeritoId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);

            if (pericia is null || !peritoAtivo || pericia.Vestigio.HashSha256 != laudoPendente.HashVestigios)
                throw new ConflitoEmissaoLaudoException(
                    "A perícia, o responsável ou o hash do vestígio mudou enquanto o laudo era emitido. Atualize a página e tente novamente.");

            var laudo = new Laudo
            {
                PericiaId = pericia.Id,
                Numero = laudoPendente.Numero,
                Versao = 1,
                Conteudo = laudoPendente.Conteudo,
                HashVestigios = laudoPendente.HashVestigios,
                HashLaudo = laudoPendente.HashLaudo,
                AssinadoPorId = laudoPendente.PeritoId,
                AssinadoEm = laudoPendente.EmitidoEm,
            };
            db.Laudos.Add(laudo);

            pericia.Situacao = SituacaoPericia.CONCLUIDA;
            pericia.ConcluidaEm = laudoPendente.EmitidoEm;
            pericia.Vestigio.Estado = EstadoVestigio.Periciado;
            pericia.Vestigio.AtualizadoEm = laudoPendente.EmitidoEm;

            await db.SaveChangesAsync(cancellationToken);

            db.Credenciais.Add(new Credencial
            {
                Tipo = TipoCredencial.COC,
                Identificador = laudoPendente.CredencialId,
                TitularId = laudoPendente.PeritoId,
                EmissorId = laudoPendente.PeritoId,
                VestigioId = pericia.VestigioId,
                EmitidaEm = laudoPendente.EmitidoEm,
                Situacao = SituacaoCredencial.PENDENTE,
            });
            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "LAUDO",
                RegistroOrigemId = laudo.Id,
                VestigioId = pericia.VestigioId,
                Evento = "LAUDO",
                PayloadJson = laudoPendente.PayloadJson,
                PayloadHashSha256 = laudoPendente.PayloadHashSha256,
                CredencialId = laudoPendente.CredencialId,
                DidResponsavel = laudoPendente.DidResponsavel,
                ChaveIdempotencia = laudoPendente.CredencialId,
                Estado = EstadoRegistroLedger.PENDENTE,
                Tentativas = 0,
                CriadoEm = laudoPendente.EmitidoEm,
                ProximaTentativaEm = laudoPendente.EmitidoEm,
            });
            db.LogsAuditoria.Add(CriarLogLaudo(laudo.Id, laudoPendente));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoEmissaoLaudoException(
                "Não foi possível emitir o laudo porque um número, credencial ou evento já existe.");
        }
    }

    private LogAuditoria CriarLogLaudo(long laudoId, LaudoPendente laudo)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "LAUDO", "LAUDO", laudoId,
            laudo.PeritoId, laudo.EmitidoEm.Ticks);

        return new LogAuditoria
        {
            IntervenienteId = laudo.PeritoId,
            Acao = "LAUDO",
            Entidade = "LAUDO",
            RegistroId = laudoId,
            DataHora = laudo.EmitidoEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
