using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.Remessa;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RemessaStore(CustodyChainDbContext db) : ICriarRemessaStore
{
    public async Task<ContextoRemessa?> ObterContextoAsync(
        long vestigioId,
        long criadorId,
        long destinoId,
        CancellationToken cancellationToken) =>
        await ObterContextoInicialAsync(vestigioId, criadorId, destinoId, cancellationToken)
        ?? await ObterContextoCustodiaAsync(vestigioId, criadorId, destinoId, cancellationToken);

    public async Task PersistirAsync(RemessaConfirmada remessa, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var vestigio = await db.Vestigios.SingleOrDefaultAsync(
                item => item.Id == remessa.VestigioId
                    && item.CustodianteAtualId == remessa.CriadorId
                    && item.AssetRef != null
                    && (remessa.TipoTransferencia == TipoTransferenciaRemessa.INICIAL
                        ? item.Estado == EstadoVestigio.Coletado
                        : item.Estado == EstadoVestigio.Recebido),
                cancellationToken);

            var destinoEhCustodiaAtiva = await EhCustodiaAtivaAsync(remessa.DestinoId, cancellationToken);
            var origemValida = remessa.TipoTransferencia == TipoTransferenciaRemessa.INICIAL
                ? await TemPerfilAsync(remessa.CriadorId, "COLETOR", cancellationToken)
                : await PossuiVcCustodiaVigenteAsync(remessa.VestigioId, remessa.CriadorId, cancellationToken);

            if (vestigio is null || !destinoEhCustodiaAtiva || !origemValida || remessa.DestinoId == remessa.CriadorId)
                throw new ConflitoRemessaException(
                    "O vestígio, a permissão ou o destino mudou enquanto a remessa era preparada. Atualize a página e tente novamente.");

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

            db.RegistrosLedger.Add(CriarRegistroLedger(movimentacao.Id, vestigio.Id, remessa));
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

    private async Task<ContextoRemessa?> ObterContextoInicialAsync(
        long vestigioId, long criadorId, long destinoId, CancellationToken cancellationToken) =>
        await (from vestigio in db.Vestigios
               join origem in db.Intervenientes.AptosParaOperacoesLedger() on criadorId equals origem.Id
               join perfilOrigem in db.Perfis on origem.PerfilId equals perfilOrigem.Id
               join destino in db.Intervenientes.AptosParaOperacoesLedger() on destinoId equals destino.Id
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
                   vestigio.Id, vestigio.AssetRef!, vestigio.ProcessoId, vestigio.RotuloEvidencia,
                   origem.Did, destino.Did, destino.Nome, TipoTransferenciaRemessa.INICIAL,
                   null, coleta.OperacaoAssinadaId!))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<ContextoRemessa?> ObterContextoCustodiaAsync(
        long vestigioId, long criadorId, long destinoId, CancellationToken cancellationToken) =>
        await (from vestigio in db.Vestigios
               join origem in db.Intervenientes.AptosParaOperacoesLedger() on criadorId equals origem.Id
               join perfilOrigem in db.Perfis on origem.PerfilId equals perfilOrigem.Id
               join credencial in db.Credenciais on origem.Id equals credencial.TitularId
               join destino in db.Intervenientes.AptosParaOperacoesLedger() on destinoId equals destino.Id
               join perfilDestino in db.Perfis on destino.PerfilId equals perfilDestino.Id
               where vestigio.Id == vestigioId
                     && vestigio.Estado == EstadoVestigio.Recebido
                     && vestigio.CustodianteAtualId == criadorId
                     && vestigio.AssetRef != null
                     && origem.Situacao == SituacaoInterveniente.ATIVO
                     && perfilOrigem.Codigo == "CUSTODIA"
                     && credencial.Tipo == TipoCredencial.PERMISSAO
                     && credencial.Situacao == SituacaoCredencial.VIGENTE
                     && credencial.ProcessoId == vestigio.ProcessoId
                     && credencial.VestigioId == vestigio.Id
                     && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
                     && destino.Situacao == SituacaoInterveniente.ATIVO
                     && perfilDestino.Codigo == "CUSTODIA"
                     && destinoId != criadorId
               orderby credencial.EmitidaEm descending
               select new ContextoRemessa(
                   vestigio.Id, vestigio.AssetRef!, vestigio.ProcessoId, vestigio.RotuloEvidencia,
                   origem.Did, destino.Did, destino.Nome, TipoTransferenciaRemessa.CUSTODIA,
                   credencial.Identificador, null))
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> EhCustodiaAtivaAsync(long intervenienteId, CancellationToken cancellationToken) =>
        (from interveniente in db.Intervenientes.AptosParaOperacoesLedger()
         join perfil in db.Perfis on interveniente.PerfilId equals perfil.Id
         where interveniente.Id == intervenienteId
               && interveniente.Situacao == SituacaoInterveniente.ATIVO
               && perfil.Codigo == "CUSTODIA"
         select interveniente.Id).AnyAsync(cancellationToken);

    private Task<bool> TemPerfilAsync(long intervenienteId, string perfilCodigo, CancellationToken cancellationToken) =>
        (from interveniente in db.Intervenientes.AptosParaOperacoesLedger()
         join perfil in db.Perfis on interveniente.PerfilId equals perfil.Id
         where interveniente.Id == intervenienteId
               && interveniente.Situacao == SituacaoInterveniente.ATIVO
               && perfil.Codigo == perfilCodigo
         select interveniente.Id).AnyAsync(cancellationToken);

    private Task<bool> PossuiVcCustodiaVigenteAsync(long vestigioId, long intervenienteId, CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join interveniente in db.Intervenientes.AptosParaOperacoesLedger() on intervenienteId equals interveniente.Id
         join perfil in db.Perfis on interveniente.PerfilId equals perfil.Id
         join credencial in db.Credenciais on vestigio.Id equals credencial.VestigioId
         where vestigio.Id == vestigioId
               && interveniente.Situacao == SituacaoInterveniente.ATIVO
               && perfil.Codigo == "CUSTODIA"
               && credencial.TitularId == intervenienteId
               && credencial.Tipo == TipoCredencial.PERMISSAO
               && credencial.Situacao == SituacaoCredencial.VIGENTE
               && credencial.ProcessoId == vestigio.ProcessoId
               && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
         select credencial.Id).AnyAsync(cancellationToken);

    private static RegistroLedger CriarRegistroLedger(long movimentacaoId, long vestigioId, RemessaConfirmada remessa) =>
        new()
        {
            EntidadeOrigem = "MOVIMENTACAO",
            RegistroOrigemId = movimentacaoId,
            VestigioId = vestigioId,
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
        };

    private LogAuditoria CriarLogRemessa(long movimentacaoId, RemessaConfirmada remessa)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "REMESSA", "MOVIMENTACAO", movimentacaoId, remessa.CriadorId, remessa.CriadoEm.Ticks);
        return new LogAuditoria
        {
            IntervenienteId = remessa.CriadorId,
            Acao = "REMESSA",
            Entidade = "MOVIMENTACAO",
            RegistroId = movimentacaoId,
            DataHora = remessa.CriadoEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
