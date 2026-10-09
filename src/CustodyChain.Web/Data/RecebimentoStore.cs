using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RecebimentoStore(CustodyChainDbContext db) : IRecebimentoStore
{
    public Task<ContextoRecebimento?> ObterContextoAsync(long movimentacaoId, long destinoId, CancellationToken cancellationToken) =>
        (from movimentacao in db.Movimentacoes
         join origem in db.Intervenientes.AptosParaOperacoesLedger() on movimentacao.OrigemId equals origem.Id
         join perfilOrigem in db.Perfis on origem.PerfilId equals perfilOrigem.Id
         join destino in db.Intervenientes.AptosParaOperacoesLedger() on movimentacao.DestinoId equals destino.Id
         join perfilDestino in db.Perfis on destino.PerfilId equals perfilDestino.Id
         join credencial in db.Credenciais on destino.Id equals credencial.TitularId
         join remessa in db.RegistrosLedger on movimentacao.Id equals remessa.RegistroOrigemId
         where movimentacao.Id == movimentacaoId
               && movimentacao.Situacao == SituacaoMovimentacao.PENDENTE
               && movimentacao.DestinoId == destinoId
               && movimentacao.Vestigio.AssetRef != null
               && origem.Situacao == SituacaoInterveniente.ATIVO
               && (perfilOrigem.Codigo == "COLETOR" || perfilOrigem.Codigo == "CUSTODIA")
               && destino.Situacao == SituacaoInterveniente.ATIVO
               && perfilDestino.Codigo == "CUSTODIA"
               && credencial.Tipo == TipoCredencial.PERMISSAO
               && credencial.Situacao == SituacaoCredencial.VIGENTE
               && credencial.ProcessoId == movimentacao.Vestigio.ProcessoId
               && credencial.VestigioId == movimentacao.VestigioId
               && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
               && remessa.Evento == "REMESSA_CRIAR"
               && remessa.Estado == EstadoRegistroLedger.ANCORADO
               && remessa.OperacaoAssinadaId != null
         orderby credencial.EmitidaEm descending
         select new ContextoRecebimento(
             movimentacao.Id,
             movimentacao.VestigioId,
             movimentacao.Vestigio.ProcessoId,
             movimentacao.Vestigio.AssetRef!,
             movimentacao.Vestigio.RotuloEvidencia,
             origem.Did,
             destino.Did,
             credencial.Identificador,
             remessa.OperacaoAssinadaId!,
             perfilOrigem.Codigo == "COLETOR" ? EstadoRetornoRecusa.Coletado : EstadoRetornoRecusa.Recebido,
             db.Lacres
                 .Where(l => l.VestigioId == movimentacao.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
                 .OrderByDescending(l => l.AplicadoEm)
                 .Select(l => l.Numero)
                 .FirstOrDefault()))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task ConfirmarAsync(RecebimentoConfirmado recebimento, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var movimentacao = await ObterMovimentacaoPendenteAsync(recebimento.MovimentacaoId, recebimento.DestinoId, cancellationToken);
            if (movimentacao is null || !await DestinoPossuiVcCustodiaVigenteAsync(movimentacao, recebimento.DestinoId, cancellationToken))
                throw new ConflitoRecebimentoException("O recebimento, a permissão ou a remessa mudou enquanto era confirmado. Atualize a página e tente novamente.");

            var lacreEsperadoAtual = await ObterNumeroLacreEsperadoAsync(movimentacao.VestigioId, cancellationToken);
            if (lacreEsperadoAtual != recebimento.NumeroLacreEsperado)
                throw new ConflitoRecebimentoException("O lacre esperado mudou enquanto era conferido. Atualize a página e tente novamente.");

            movimentacao.DataHoraChegada = recebimento.RecebidoEm;
            movimentacao.CondicoesAdequadas = recebimento.LacreConfere;
            movimentacao.AprovadoPorId = recebimento.DestinoId;
            movimentacao.Situacao = SituacaoMovimentacao.ACEITA;
            movimentacao.Vestigio.Estado = recebimento.LacreConfere ? EstadoVestigio.Recebido : EstadoVestigio.CustodiaComprometida;
            movimentacao.Vestigio.EtapaAtual = 7;
            movimentacao.Vestigio.CustodianteAtualId = recebimento.DestinoId;
            movimentacao.Vestigio.AtualizadoEm = recebimento.RecebidoEm;

            db.RegistrosLedger.Add(CriarRegistroLedger(movimentacao.Id, movimentacao.VestigioId, "REMESSA_RECEBER", recebimento.RecebidoEm,
                recebimento.OperacaoAssinadaJson, recebimento.OperacaoAssinadaId, recebimento.OperacaoAssinadaHashSha256, recebimento.DidResponsavel));
            db.LogsAuditoria.Add(CriarLog(movimentacao.Id, "REMESSA_RECEBER", recebimento.DestinoId, recebimento.RecebidoEm));
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRecebimentoException("Não foi possível concluir o recebimento porque a remessa ou o registro de autorização já existe.");
        }
    }

    public async Task RecusarAsync(RecebimentoRecusado recebimento, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var movimentacao = await ObterMovimentacaoPendenteAsync(recebimento.MovimentacaoId, recebimento.DestinoId, cancellationToken);
            if (movimentacao is null || movimentacao.OrigemId is null
                || !await DestinoPossuiVcCustodiaVigenteAsync(movimentacao, recebimento.DestinoId, cancellationToken))
            {
                throw new ConflitoRecebimentoException("O recebimento, a permissão ou a remessa mudou enquanto era recusado. Atualize a página e tente novamente.");
            }

            movimentacao.Situacao = SituacaoMovimentacao.RECUSADA;
            movimentacao.MotivoRecusa = recebimento.MotivoRecusa;
            movimentacao.DataHoraChegada = recebimento.RecusadoEm;
            movimentacao.AprovadoPorId = recebimento.DestinoId;
            movimentacao.Vestigio.Estado = recebimento.EstadoAposRecusa == EstadoRetornoRecusa.Coletado
                ? EstadoVestigio.Coletado : EstadoVestigio.Recebido;
            movimentacao.Vestigio.CustodianteAtualId = movimentacao.OrigemId;
            movimentacao.Vestigio.AtualizadoEm = recebimento.RecusadoEm;

            db.RegistrosLedger.Add(CriarRegistroLedger(movimentacao.Id, movimentacao.VestigioId, "REMESSA_RECUSAR", recebimento.RecusadoEm,
                recebimento.OperacaoAssinadaJson, recebimento.OperacaoAssinadaId, recebimento.OperacaoAssinadaHashSha256, recebimento.DidResponsavel));
            db.LogsAuditoria.Add(CriarLog(movimentacao.Id, "REMESSA_RECUSAR", recebimento.DestinoId, recebimento.RecusadoEm));
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRecebimentoException("Não foi possível recusar o recebimento porque a remessa ou o registro de autorização já existe.");
        }
    }

    private Task<Movimentacao?> ObterMovimentacaoPendenteAsync(long movimentacaoId, long destinoId, CancellationToken cancellationToken) =>
        db.Movimentacoes.Include(m => m.Vestigio).SingleOrDefaultAsync(m => m.Id == movimentacaoId
            && m.DestinoId == destinoId && m.Situacao == SituacaoMovimentacao.PENDENTE && m.Vestigio.AssetRef != null, cancellationToken);

    private Task<bool> DestinoPossuiVcCustodiaVigenteAsync(Movimentacao movimentacao, long destinoId, CancellationToken cancellationToken) =>
        (from destino in db.Intervenientes.AptosParaOperacoesLedger()
         join perfil in db.Perfis on destino.PerfilId equals perfil.Id
         join credencial in db.Credenciais on destino.Id equals credencial.TitularId
         where destino.Id == destinoId && destino.Situacao == SituacaoInterveniente.ATIVO && perfil.Codigo == "CUSTODIA"
               && credencial.Tipo == TipoCredencial.PERMISSAO && credencial.Situacao == SituacaoCredencial.VIGENTE
               && credencial.ProcessoId == movimentacao.Vestigio.ProcessoId && credencial.VestigioId == movimentacao.VestigioId
               && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
         select credencial.Id).AnyAsync(cancellationToken);

    private Task<string?> ObterNumeroLacreEsperadoAsync(long vestigioId, CancellationToken cancellationToken) =>
        db.Lacres.Where(l => l.VestigioId == vestigioId && l.Situacao == SituacaoLacre.INTACTO)
            .OrderByDescending(l => l.AplicadoEm).Select(l => l.Numero).FirstOrDefaultAsync(cancellationToken);

    private static RegistroLedger CriarRegistroLedger(long movimentacaoId, long vestigioId, string evento, DateTime ocorridoEm,
        string operacaoJson, string operacaoId, string operacaoHash, string didResponsavel) => new()
    {
        EntidadeOrigem = "MOVIMENTACAO", RegistroOrigemId = movimentacaoId, VestigioId = vestigioId, Evento = evento,
        PayloadJson = operacaoJson, PayloadHashSha256 = operacaoHash, DidResponsavel = didResponsavel,
        ChaveIdempotencia = operacaoId, OperacaoAssinadaId = operacaoId, VersaoOperacaoAssinada = 1,
        OperacaoAssinadaJson = operacaoJson, OperacaoAssinadaHashSha256 = operacaoHash,
        Estado = EstadoRegistroLedger.ANCORADO, Tentativas = 1, CriadoEm = ocorridoEm, AncoradoEm = ocorridoEm
    };

    private LogAuditoria CriarLog(long movimentacaoId, string acao, long responsavelId, DateTime ocorridoEm)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id).Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, acao, "MOVIMENTACAO", movimentacaoId, responsavelId, ocorridoEm.Ticks);
        return new LogAuditoria
        {
            IntervenienteId = responsavelId, Acao = acao, Entidade = "MOVIMENTACAO", RegistroId = movimentacaoId, DataHora = ocorridoEm,
            HashAnterior = hashAnterior, HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)))
        };
    }
}
