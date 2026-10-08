using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RemessaStore(CustodyChainDbContext db) : ICriarRemessaStore
{
    public Task<ContextoRemessa?> ObterContextoAsync(
        long vestigioId,
        long criadorId,
        long destinoId,
        CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join origem in db.Intervenientes on criadorId equals origem.Id
         join perfilOrigem in db.Perfis on origem.PerfilId equals perfilOrigem.Id
         join destino in db.Intervenientes on destinoId equals destino.Id
         join perfilDestino in db.Perfis on destino.PerfilId equals perfilDestino.Id
         join coleta in db.RegistrosLedger on vestigio.Id equals coleta.VestigioId
         where vestigio.Id == vestigioId
               && vestigio.Estado == EstadoVestigio.Coletado
               && vestigio.CustodianteAtualId == criadorId
               && vestigio.AssetRef != null
               && origem.Situacao == SituacaoInterveniente.ATIVO
               && perfilOrigem.Codigo == "COLETOR"
               && destino.Situacao == SituacaoInterveniente.ATIVO
               && perfilDestino.Codigo == "CUSTODIA"
               && destinoId != criadorId
               && coleta.Evento == "COLETA_REGISTRAR"
               && coleta.Estado == EstadoRegistroLedger.ANCORADO
               && coleta.OperacaoAssinadaId != null
         select new ContextoRemessa(
             vestigio.Id,
             vestigio.AssetRef!,
             vestigio.ProcessoId,
             vestigio.RotuloEvidencia,
             origem.Did,
             destino.Did,
             destino.Nome,
             coleta.OperacaoAssinadaId!))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task PersistirAsync(RemessaConfirmada remessa, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var vestigio = await db.Vestigios.SingleOrDefaultAsync(
                vestigio => vestigio.Id == remessa.VestigioId
                    && vestigio.Estado == EstadoVestigio.Coletado
                    && vestigio.CustodianteAtualId == remessa.CriadorId
                    && vestigio.AssetRef != null,
                cancellationToken);

            var destinoEhCustodiaAtiva = await (from destino in db.Intervenientes
                                                 join perfil in db.Perfis on destino.PerfilId equals perfil.Id
                                                 where destino.Id == remessa.DestinoId
                                                       && destino.Situacao == SituacaoInterveniente.ATIVO
                                                       && perfil.Codigo == "CUSTODIA"
                                                 select destino.Id)
                .AnyAsync(cancellationToken);

            if (vestigio is null || !destinoEhCustodiaAtiva || remessa.DestinoId == remessa.CriadorId)
                throw new ConflitoRemessaException(
                    "O vestígio ou destino mudou enquanto a remessa era preparada. Atualize a página e tente novamente.");

            var movimentacao = new Movimentacao
            {
                VestigioId = vestigio.Id,
                Tipo = TipoMovimentacao.TRANSPORTE,
                Etapa = 6,
                OrigemId = remessa.CriadorId,
                DestinoId = remessa.DestinoId,
                DataHoraSaida = remessa.DataHoraSaida,
                CodigoRastreamento = remessa.CodigoRastreamento,
                Situacao = SituacaoMovimentacao.PENDENTE,
                CriadoPorId = remessa.CriadorId,
            };
            db.Movimentacoes.Add(movimentacao);

            vestigio.Estado = EstadoVestigio.EmTransporte;
            vestigio.EtapaAtual = 6;
            vestigio.AtualizadoEm = remessa.CriadoEm;

            await db.SaveChangesAsync(cancellationToken);

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "MOVIMENTACAO",
                RegistroOrigemId = movimentacao.Id,
                VestigioId = vestigio.Id,
                Evento = "REMESSA_CRIAR",
                PayloadJson = remessa.OperacaoAssinadaJson,
                PayloadHashSha256 = remessa.OperacaoAssinadaHashSha256,
                DidResponsavel = remessa.DidResponsavel,
                ChaveIdempotencia = remessa.OperacaoAssinadaId,
                OperacaoAssinadaId = remessa.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = remessa.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = remessa.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = remessa.CriadoEm,
                AncoradoEm = remessa.ConfirmadoEm,
            });

            db.LogsAuditoria.Add(CriarLogRemessa(movimentacao.Id, remessa));
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRemessaException("Não foi possível concluir a remessa porque uma operação já existe.");
        }
    }

    private LogAuditoria CriarLogRemessa(long movimentacaoId, RemessaConfirmada remessa)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "REMESSA", "MOVIMENTACAO", movimentacaoId, remessa.CriadorId, remessa.CriadoEm.Ticks);
        var hashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));
        return new LogAuditoria
        {
            IntervenienteId = remessa.CriadorId,
            Acao = "REMESSA",
            Entidade = "MOVIMENTACAO",
            RegistroId = movimentacaoId,
            DataHora = remessa.CriadoEm,
            HashAnterior = hashAnterior,
            HashRegistro = hashRegistro,
        };
    }
}
