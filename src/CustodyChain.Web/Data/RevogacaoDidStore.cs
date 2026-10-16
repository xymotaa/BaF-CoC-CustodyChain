using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.RevogacaoDid;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RevogacaoDidStore(CustodyChainDbContext db) : IRevogacaoDidStore
{
    public async Task<ContextoRevogacaoDid?> ObterContextoAsync(long intervenienteId, CancellationToken cancellationToken = default)
    {
        var alvo = await db.Intervenientes.AsNoTracking().FirstOrDefaultAsync(i => i.Id == intervenienteId, cancellationToken);
        if (alvo is null) return null;
        return new ContextoRevogacaoDid(alvo.Id, 0, alvo.Did, alvo.Nome, alvo.Situacao == SituacaoInterveniente.ATIVO,
            await PossuiResponsabilidadesPendentesAsync(alvo.Id, cancellationToken));
    }

    public async Task<SagaRevogacaoDidDto?> ObterPorCommandIdAsync(string commandId, CancellationToken cancellationToken = default)
    {
        var saga = await db.SagasRevogacaoDid.AsNoTracking().Include(s => s.Interveniente)
            .SingleOrDefaultAsync(s => s.CommandId == commandId, cancellationToken);
        return saga is null ? null : Mapear(saga);
    }

    public async Task RegistrarRejeicaoAsync(RejeicaoRevogacaoDid rejeicao, CancellationToken cancellationToken = default)
    {
        await AdicionarAuditoriaAsync(AcaoRejeicao(rejeicao.Motivo), rejeicao.IntervenienteId, rejeicao.AdministradorId,
            rejeicao.Auditoria, rejeicao.OcorridaEm, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SagaRevogacaoDidDto> CriarOuObterAsync(NovaSagaRevogacaoDid nova, CancellationToken cancellationToken = default)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        var existente = await db.SagasRevogacaoDid.Include(s => s.Interveniente)
            .SingleOrDefaultAsync(s => s.CommandId == nova.CommandId, cancellationToken);
        if (existente is not null)
        {
            if (!string.Equals(existente.CommandJson, nova.CommandJson, StringComparison.Ordinal))
                throw new RevogacaoDidInvalidaException("commandId já foi usado com conteúdo diferente.");
            await transacao.CommitAsync(cancellationToken);
            return Mapear(existente);
        }

        var alvo = await db.Intervenientes.SingleOrDefaultAsync(i => i.Id == nova.IntervenienteId, cancellationToken)
            ?? throw new RevogacaoDidInvalidaException("Interveniente não encontrado.");
        if (alvo.Situacao != SituacaoInterveniente.ATIVO || await PossuiResponsabilidadesPendentesAsync(alvo.Id, cancellationToken))
            throw new RevogacaoDidInvalidaException("Interveniente não está disponível para revogação.");

        var saga = new SagaRevogacaoDid
        {
            CommandId = nova.CommandId, IntervenienteId = nova.IntervenienteId, AdministradorId = nova.AdministradorId,
            DidAlvo = nova.DidAlvo, CommandJson = nova.CommandJson, KeyId = nova.KeyId, Signature = nova.Signature,
            Estado = EstadoSagaRevogacaoDid.PENDENTE_LEDGER, CriadaEm = nova.CriadaEm, ProximaTentativaEm = nova.CriadaEm,
            Interveniente = alvo
        };
        db.SagasRevogacaoDid.Add(saga);
        alvo.Situacao = SituacaoInterveniente.REVOGACAO_PENDENTE;
        await AdicionarAuditoriaAsync("DID_REVOGACAO_PREPARADA", alvo.Id, nova.AdministradorId, nova.Auditoria, nova.CriadaEm, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return Mapear(saga, alvo.Nome);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var concorrente = await db.SagasRevogacaoDid.AsNoTracking().Include(s => s.Interveniente)
                .SingleOrDefaultAsync(s => s.CommandId == nova.CommandId, cancellationToken);
            if (concorrente is null) throw;
            if (!string.Equals(concorrente.CommandJson, nova.CommandJson, StringComparison.Ordinal))
                throw new RevogacaoDidInvalidaException("commandId já foi usado com conteúdo diferente.");
            return Mapear(concorrente);
        }
    }

    public Task<SagaRevogacaoDidDto?> TentarAssumirAsync(string commandId, DateTime agora, TimeSpan lease, CancellationToken cancellationToken = default) =>
        TentarAssumirInternamenteAsync(s => s.CommandId == commandId, agora, lease, cancellationToken);

    public async Task<IReadOnlyList<SagaRevogacaoDidDto>> AssumirPendenciasAsync(DateTime agora, TimeSpan lease, int limite, CancellationToken cancellationToken = default)
    {
        var ids = await db.SagasRevogacaoDid.AsNoTracking().Where(s =>
                (s.Estado == EstadoSagaRevogacaoDid.PENDENTE_LEDGER || s.Estado == EstadoSagaRevogacaoDid.FALHA_RECONCILIACAO || s.Estado == EstadoSagaRevogacaoDid.PROCESSANDO_LEDGER)
                && s.ProximaTentativaEm != null && s.ProximaTentativaEm <= agora)
            .OrderBy(s => s.CriadaEm).Select(s => s.CommandId).Take(limite).ToListAsync(cancellationToken);
        var assumidas = new List<SagaRevogacaoDidDto>();
        foreach (var id in ids)
        {
            var saga = await TentarAssumirAsync(id, agora, lease, cancellationToken);
            if (saga is not null) assumidas.Add(saga);
        }
        return assumidas;
    }

    public async Task ConfirmarAsync(string commandId, DateTime confirmadoEm, bool reconciliada, CancellationToken cancellationToken = default)
    {
        var saga = await ObterSagaParaAtualizacaoAsync(commandId, cancellationToken);
        if (saga.Estado == EstadoSagaRevogacaoDid.CONFIRMADA_NO_LEDGER) return;
        saga.Estado = EstadoSagaRevogacaoDid.CONFIRMADA_NO_LEDGER;
        saga.ConfirmadaEm = confirmadoEm; saga.UltimaTentativaEm = confirmadoEm; saga.ProximaTentativaEm = null; saga.Erro = null;
        saga.Interveniente.Situacao = SituacaoInterveniente.REVOGADO;
        saga.Interveniente.SituacaoIdentidadeLedger = SituacaoIdentidadeLedger.INATIVA;
        await AdicionarAuditoriaAsync(reconciliada ? "DID_REVOGACAO_RECONCILIADA" : "DID_REVOGACAO_CONFIRMADA",
            saga.IntervenienteId, saga.AdministradorId, new ContextoAuditoriaRevogacaoDid(null, null), confirmadoEm, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RegistrarFalhaTransitoriaAsync(string commandId, string erro, DateTime agora, DateTime proximaTentativaEm, CancellationToken cancellationToken = default)
    {
        var saga = await ObterSagaParaAtualizacaoAsync(commandId, cancellationToken);
        saga.Estado = EstadoSagaRevogacaoDid.FALHA_RECONCILIACAO; saga.Erro = erro; saga.UltimaTentativaEm = agora; saga.ProximaTentativaEm = proximaTentativaEm;
        if (saga.Tentativas < byte.MaxValue) saga.Tentativas++;
        await AdicionarAuditoriaAsync("DID_REVOGACAO_RECONCILIACAO_FALHOU", saga.IntervenienteId, saga.AdministradorId,
            new ContextoAuditoriaRevogacaoDid(null, null), agora, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RegistrarFalhaDefinitivaAsync(string commandId, string erro, DateTime agora, CancellationToken cancellationToken = default)
    {
        var saga = await ObterSagaParaAtualizacaoAsync(commandId, cancellationToken);
        saga.Estado = EstadoSagaRevogacaoDid.FALHA_PERMANENTE; saga.Erro = erro; saga.UltimaTentativaEm = agora; saga.ProximaTentativaEm = null;
        if (saga.Tentativas < byte.MaxValue) saga.Tentativas++;
        saga.Interveniente.Situacao = SituacaoInterveniente.ATIVO;
        await AdicionarAuditoriaAsync("DID_REVOGACAO_REJEITADA", saga.IntervenienteId, saga.AdministradorId,
            new ContextoAuditoriaRevogacaoDid(null, null), agora, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<SagaRevogacaoDidDto?> TentarAssumirInternamenteAsync(System.Linq.Expressions.Expression<Func<SagaRevogacaoDid, bool>> filtro, DateTime agora, TimeSpan lease, CancellationToken cancellationToken)
    {
        var atualizadas = await db.SagasRevogacaoDid.Where(filtro).Where(s =>
                (s.Estado == EstadoSagaRevogacaoDid.PENDENTE_LEDGER || s.Estado == EstadoSagaRevogacaoDid.FALHA_RECONCILIACAO || s.Estado == EstadoSagaRevogacaoDid.PROCESSANDO_LEDGER)
                && s.ProximaTentativaEm != null && s.ProximaTentativaEm <= agora)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Estado, EstadoSagaRevogacaoDid.PROCESSANDO_LEDGER)
                .SetProperty(s => s.UltimaTentativaEm, agora)
                .SetProperty(s => s.ProximaTentativaEm, agora.Add(lease)), cancellationToken);
        if (atualizadas == 0) return null;
        var saga = await db.SagasRevogacaoDid.AsNoTracking().Include(s => s.Interveniente).Where(filtro).SingleAsync(cancellationToken);
        return Mapear(saga);
    }

    private async Task<SagaRevogacaoDid> ObterSagaParaAtualizacaoAsync(string commandId, CancellationToken cancellationToken) =>
        await db.SagasRevogacaoDid.Include(s => s.Interveniente).SingleAsync(s => s.CommandId == commandId, cancellationToken);

    private async Task<bool> PossuiResponsabilidadesPendentesAsync(long id, CancellationToken cancellationToken) =>
        await db.Vestigios.AnyAsync(v => v.CustodianteAtualId == id, cancellationToken)
        || await db.Movimentacoes.AnyAsync(m => m.Situacao == SituacaoMovimentacao.PENDENTE && (m.OrigemId == id || m.DestinoId == id || m.CriadoPorId == id), cancellationToken)
        || await db.Pericias.AnyAsync(p => p.PeritoId == id && p.Situacao != SituacaoPericia.CONCLUIDA && p.Situacao != SituacaoPericia.RECUSADA, cancellationToken)
        || await db.Descartes.AnyAsync(d => d.ExecutadoEm == null && (d.SolicitadoPorId == id || d.AprovadoPorId == id), cancellationToken);

    private async Task AdicionarAuditoriaAsync(string acao, long alvoId, long administradorId, ContextoAuditoriaRevogacaoDid contexto, DateTime agora, CancellationToken cancellationToken)
    {
        var anterior = await db.LogsAuditoria.OrderByDescending(log => log.Id).Select(log => log.HashRegistro).FirstOrDefaultAsync(cancellationToken) ?? new string('0', 64);
        var conteudo = string.Join('|', anterior, acao, "INTERVENIENTE", alvoId, administradorId, agora.Ticks);
        db.LogsAuditoria.Add(new LogAuditoria { IntervenienteId = administradorId, Acao = acao, Entidade = "INTERVENIENTE", RegistroId = alvoId,
            DataHora = agora, Ip = contexto.Ip, UserAgent = contexto.UserAgent, HashAnterior = anterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))) });
    }

    private static string AcaoRejeicao(MotivoRejeicaoRevogacaoDid motivo) => motivo switch
    {
        MotivoRejeicaoRevogacaoDid.AutoRevogacao => "DID_REVOGACAO_REJ_AUTO",
        MotivoRejeicaoRevogacaoDid.ResponsabilidadesPendentes => "DID_REVOGACAO_REJ_PEND",
        MotivoRejeicaoRevogacaoDid.ComandoInvalido => "DID_REVOGACAO_REJ_COMANDO",
        _ => "DID_REVOGACAO_REJ_INDISP"
    };

    private static SagaRevogacaoDidDto Mapear(SagaRevogacaoDid saga, string? nome = null) =>
        new(saga.CommandId, saga.DidAlvo, saga.CommandJson, saga.KeyId, saga.Signature, saga.Estado switch
        {
            EstadoSagaRevogacaoDid.PENDENTE_LEDGER => EstadoRevogacaoDid.PendenteLedger,
            EstadoSagaRevogacaoDid.PROCESSANDO_LEDGER => EstadoRevogacaoDid.ProcessandoLedger,
            EstadoSagaRevogacaoDid.CONFIRMADA_NO_LEDGER => EstadoRevogacaoDid.ConfirmadaNoLedger,
            EstadoSagaRevogacaoDid.FALHA_PERMANENTE => EstadoRevogacaoDid.FalhaPermanente,
            _ => EstadoRevogacaoDid.FalhaReconciliacao
        }, nome ?? saga.Interveniente?.Nome ?? string.Empty);
}
