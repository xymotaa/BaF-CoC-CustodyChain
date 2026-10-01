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
         join destino in db.Intervenientes on destinoId equals destino.Id
         join perfilDestino in db.Perfis on destino.PerfilId equals perfilDestino.Id
         where vestigio.Id == vestigioId
             && vestigio.Estado == EstadoVestigio.Coletado
             && vestigio.CustodianteAtualId == criadorId
             && origem.Situacao == SituacaoInterveniente.ATIVO
             && destino.Situacao == SituacaoInterveniente.ATIVO
             && destinoId != criadorId
         select new ContextoRemessa(
             vestigio.Id,
             vestigio.RotuloEvidencia,
             origem.Did,
             destino.Did,
             destino.Nome))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task PersistirAsync(RemessaPendente remessa, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var vestigio = await db.Vestigios.SingleOrDefaultAsync(
                v => v.Id == remessa.VestigioId
                    && v.Estado == EstadoVestigio.Coletado
                    && v.CustodianteAtualId == remessa.CriadorId,
                cancellationToken);

            var destinoAtivo = await db.Intervenientes.AnyAsync(
                i => i.Id == remessa.DestinoId && i.Situacao == SituacaoInterveniente.ATIVO,
                cancellationToken);

            if (vestigio is null || !destinoAtivo || remessa.DestinoId == remessa.CriadorId)
            {
                throw new ConflitoRemessaException(
                    "O vestígio ou destino mudou enquanto a remessa era preparada. Atualize a página e tente novamente.");
            }

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

            db.Credenciais.Add(new Credencial
            {
                Tipo = TipoCredencial.COC,
                Identificador = remessa.CredencialId,
                TitularId = remessa.CriadorId,
                EmissorId = remessa.CriadorId,
                VestigioId = vestigio.Id,
                EmitidaEm = remessa.CriadoEm,
                Situacao = SituacaoCredencial.PENDENTE,
            });

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "MOVIMENTACAO",
                RegistroOrigemId = movimentacao.Id,
                VestigioId = vestigio.Id,
                Evento = "REMESSA",
                PayloadJson = remessa.PayloadJson,
                PayloadHashSha256 = remessa.PayloadHashSha256,
                CredencialId = remessa.CredencialId,
                DidResponsavel = remessa.DidResponsavel,
                ChaveIdempotencia = remessa.CredencialId,
                Estado = EstadoRegistroLedger.PENDENTE,
                Tentativas = 0,
                CriadoEm = remessa.CriadoEm,
                ProximaTentativaEm = remessa.CriadoEm,
            });

            db.LogsAuditoria.Add(CriarLogRemessa(movimentacao.Id, remessa));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRemessaException(
                "Não foi possível concluir a remessa porque uma credencial ou evento já existe.");
        }
    }

    private LogAuditoria CriarLogRemessa(long movimentacaoId, RemessaPendente remessa)
    {
        var hashAnterior = db.LogsAuditoria
            .OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro)
            .FirstOrDefault() ?? new string('0', 64);
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
