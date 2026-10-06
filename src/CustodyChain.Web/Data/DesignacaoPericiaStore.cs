using CustodyChain.Web.Application.Pericias;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class DesignacaoPericiaStore(CustodyChainDbContext db) : IDesignacaoPericiaStore
{
    private static readonly SituacaoPericia[] SituacoesAtivas =
    [
        SituacaoPericia.SOLICITADA,
        SituacaoPericia.DESIGNADA,
        SituacaoPericia.RECEBIDA,
        SituacaoPericia.EM_EXECUCAO
    ];

    public Task<ContextoSolicitacaoDesignacaoPericia?> ObterContextoSolicitacaoAsync(
        long vestigioId,
        long peritoId,
        long solicitanteId,
        CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join perito in db.Intervenientes.Include(i => i.Perfil) on peritoId equals perito.Id
         join solicitante in db.Intervenientes.Include(i => i.Perfil) on solicitanteId equals solicitante.Id
         where vestigio.Id == vestigioId
             && vestigio.Estado == EstadoVestigio.Armazenado
             && perito.Situacao == SituacaoInterveniente.ATIVO
             && perito.Perfil.Codigo == "PERITO"
             && solicitante.Situacao == SituacaoInterveniente.ATIVO
             && solicitante.Perfil.Codigo == "CUSTODIA"
             && !db.Pericias.Any(p => p.VestigioId == vestigio.Id && SituacoesAtivas.Contains(p.Situacao))
         select new ContextoSolicitacaoDesignacaoPericia(
             vestigio.Id,
             vestigio.ProcessoId,
             vestigio.RotuloEvidencia,
             perito.Id,
             perito.Nome))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<long> PersistirSolicitacaoAsync(
        SolicitacaoDesignacaoPericiaPendente solicitacao,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var vestigioDisponivel = await db.Vestigios.AnyAsync(v =>
                v.Id == solicitacao.VestigioId
                && v.ProcessoId == solicitacao.ProcessoId
                && v.Estado == EstadoVestigio.Armazenado,
                cancellationToken);
            var peritoDisponivel = await db.Intervenientes.Include(i => i.Perfil).AnyAsync(i =>
                i.Id == solicitacao.PeritoId
                && i.Situacao == SituacaoInterveniente.ATIVO
                && i.Perfil.Codigo == "PERITO",
                cancellationToken);
            var solicitanteDisponivel = await db.Intervenientes.Include(i => i.Perfil).AnyAsync(i =>
                i.Id == solicitacao.SolicitanteId
                && i.Situacao == SituacaoInterveniente.ATIVO
                && i.Perfil.Codigo == "CUSTODIA",
                cancellationToken);
            var jaExiste = await db.Pericias.AnyAsync(p =>
                p.VestigioId == solicitacao.VestigioId && SituacoesAtivas.Contains(p.Situacao),
                cancellationToken);

            if (!vestigioDisponivel || !peritoDisponivel || !solicitanteDisponivel || jaExiste)
                throw new ConflitoDesignacaoPericiaException(
                    "O vestígio ou o perito mudou enquanto a solicitação era preparada. Atualize a página e tente novamente.");

            var pericia = new Pericia
            {
                VestigioId = solicitacao.VestigioId,
                ProcessoId = solicitacao.ProcessoId,
                PeritoId = solicitacao.PeritoId,
                AreaPericial = solicitacao.AreaPericial,
                Prioridade = Enum.Parse<PrioridadePericia>(solicitacao.Prioridade),
                SolicitadaEm = solicitacao.SolicitadaEm,
                Situacao = SituacaoPericia.SOLICITADA,
            };
            db.Pericias.Add(pericia);
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return pericia.Id;
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoDesignacaoPericiaException(
                "Não foi possível registrar a solicitação de perícia.");
        }
    }

    public async Task<IReadOnlyList<SolicitacaoDesignacaoPericiaResumo>> ListarPendentesAsync(
        long aprovadorId,
        CancellationToken cancellationToken)
    {
        if (!await EhAdministradorAtivoAsync(aprovadorId, cancellationToken))
            return [];

        return await db.Pericias
            .Where(p => p.Situacao == SituacaoPericia.SOLICITADA)
            .OrderBy(p => p.SolicitadaEm)
            .Select(p => new SolicitacaoDesignacaoPericiaResumo(
                p.Id,
                p.Vestigio.RotuloEvidencia,
                p.Perito!.Nome,
                p.AreaPericial,
                p.Prioridade.ToString(),
                p.SolicitadaEm))
            .ToListAsync(cancellationToken);
    }

    public async Task<SolicitacaoDesignacaoPericiaDetalhe?> ObterDetalheAsync(
        long periciaId,
        long aprovadorId,
        CancellationToken cancellationToken)
    {
        if (!await EhAdministradorAtivoAsync(aprovadorId, cancellationToken))
            return null;

        return await db.Pericias
            .Where(p => p.Id == periciaId && p.Situacao == SituacaoPericia.SOLICITADA)
            .Select(p => new SolicitacaoDesignacaoPericiaDetalhe(
                p.Id,
                p.Vestigio.RotuloEvidencia,
                p.Vestigio.Descricao,
                p.Perito!.Nome,
                p.Perito.Did,
                p.AreaPericial,
                p.Prioridade.ToString(),
                p.SolicitadaEm))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ContextoAprovacaoDesignacaoPericia?> PrepararAprovacaoAsync(
        PreparacaoCredencialDesignacaoPericia preparacao,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await ObterPericiaAsync(preparacao.PericiaId, cancellationToken);
            var aprovador = await db.Intervenientes.Include(i => i.Perfil).SingleOrDefaultAsync(i =>
                i.Id == preparacao.AprovadorId
                && i.Situacao == SituacaoInterveniente.ATIVO
                && i.Perfil.Codigo == "ADMIN",
                cancellationToken);
            if (!PodePreparar(pericia, aprovador))
                return null;

            if (pericia!.Credencial is null)
            {
                var credencial = new Credencial
                {
                    Tipo = TipoCredencial.PERMISSAO,
                    Identificador = preparacao.IdentificadorCredencial,
                    TitularId = pericia.PeritoId!.Value,
                    EmissorId = preparacao.AprovadorId,
                    ProcessoId = pericia.ProcessoId,
                    VestigioId = pericia.VestigioId,
                    EmitidaEm = preparacao.EmitidaEm,
                    ValidaAte = preparacao.ValidaAte,
                    Situacao = SituacaoCredencial.PENDENTE,
                };
                db.Credenciais.Add(credencial);
                await db.SaveChangesAsync(cancellationToken);
                pericia.CredencialId = credencial.Id;
                await db.SaveChangesAsync(cancellationToken);
                pericia.Credencial = credencial;
            }

            if (pericia.Credencial.EmissorId != preparacao.AprovadorId
                || pericia.Credencial.Tipo != TipoCredencial.PERMISSAO
                || pericia.Credencial.Situacao is not (SituacaoCredencial.PENDENTE or SituacaoCredencial.VIGENTE))
                throw new ConflitoDesignacaoPericiaException(
                    "A solicitação já está vinculada a outra aprovação de credencial.");

            var contexto = CriarContexto(pericia, aprovador!);
            await transacao.CommitAsync(cancellationToken);
            return contexto;
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoDesignacaoPericiaException(
                "Não foi possível preparar a credencial da perícia.");
        }
    }

    public async Task<ContextoAprovacaoDesignacaoPericia?> ObterContextoAprovacaoAsync(
        long periciaId,
        long aprovadorId,
        CancellationToken cancellationToken)
    {
        var pericia = await ObterPericiaAsync(periciaId, cancellationToken);
        var aprovador = await db.Intervenientes.Include(i => i.Perfil).SingleOrDefaultAsync(i =>
            i.Id == aprovadorId
            && i.Situacao == SituacaoInterveniente.ATIVO
            && i.Perfil.Codigo == "ADMIN",
            cancellationToken);

        return pericia?.Credencial is not null
            && aprovador is not null
            && pericia.Credencial.EmissorId == aprovadorId
            && ((pericia.Situacao == SituacaoPericia.SOLICITADA
                    && pericia.Credencial.Situacao == SituacaoCredencial.PENDENTE)
                || (pericia.Situacao == SituacaoPericia.DESIGNADA
                    && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE))
            ? CriarContexto(pericia, aprovador)
            : null;
    }

    public async Task ConcluirAprovacaoAsync(
        long periciaId,
        long aprovadorId,
        string identificadorCredencial,
        DateTime concluidaEm,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await ObterPericiaAsync(periciaId, cancellationToken);
            var aprovadorAtivo = await EhAdministradorAtivoAsync(aprovadorId, cancellationToken);
            if (pericia?.Credencial is null || !aprovadorAtivo
                || pericia.Credencial.EmissorId != aprovadorId
                || pericia.Credencial.Identificador != identificadorCredencial)
                throw new ConflitoDesignacaoPericiaException(
                    "A solicitação ou a credencial mudou durante a confirmação.");

            if (pericia.Situacao == SituacaoPericia.DESIGNADA
                && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE)
            {
                await transacao.CommitAsync(cancellationToken);
                return;
            }

            if (pericia.Situacao != SituacaoPericia.SOLICITADA
                || pericia.Credencial.Situacao != SituacaoCredencial.PENDENTE
                || pericia.Vestigio.Estado != EstadoVestigio.Armazenado
                || pericia.Perito?.Situacao != SituacaoInterveniente.ATIVO
                || pericia.Credencial.ValidaAte is not null && pericia.Credencial.ValidaAte <= concluidaEm)
                throw new ConflitoDesignacaoPericiaException(
                    "A perícia, o vestígio, o perito ou a validade mudou durante a aprovação.");

            pericia.Credencial.Situacao = SituacaoCredencial.VIGENTE;
            pericia.Situacao = SituacaoPericia.DESIGNADA;
            pericia.Vestigio.Estado = EstadoVestigio.EmPericia;
            pericia.Vestigio.EtapaAtual = 8;
            pericia.Vestigio.AtualizadoEm = concluidaEm;

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoDesignacaoPericiaException(
                "O ledger confirmou a VC, mas o estado local não pôde ser concluído. Reenvie a mesma prova.");
        }
    }

    private Task<Pericia?> ObterPericiaAsync(long periciaId, CancellationToken cancellationToken) =>
        db.Pericias
            .Include(p => p.Vestigio)
            .Include(p => p.Perito)
            .Include(p => p.Credencial)
            .SingleOrDefaultAsync(p => p.Id == periciaId, cancellationToken);

    private Task<bool> EhAdministradorAtivoAsync(long intervenienteId, CancellationToken cancellationToken) =>
        db.Intervenientes.Include(i => i.Perfil).AnyAsync(i =>
            i.Id == intervenienteId
            && i.Situacao == SituacaoInterveniente.ATIVO
            && i.Perfil.Codigo == "ADMIN",
            cancellationToken);

    private static bool PodePreparar(Pericia? pericia, Interveniente? aprovador) =>
        pericia is not null
        && aprovador is not null
        && pericia.Perito is { Situacao: SituacaoInterveniente.ATIVO }
        && ((pericia.Situacao == SituacaoPericia.SOLICITADA
                && pericia.Vestigio.Estado == EstadoVestigio.Armazenado)
            || (pericia.Situacao == SituacaoPericia.DESIGNADA
                && pericia.Credencial?.Situacao == SituacaoCredencial.VIGENTE));

    private static ContextoAprovacaoDesignacaoPericia CriarContexto(
        Pericia pericia,
        Interveniente aprovador) =>
        new(
            pericia.Id,
            pericia.Credencial!.Identificador,
            pericia.Credencial.EmitidaEm,
            pericia.Credencial.ValidaAte,
            aprovador.Did,
            pericia.Perito!.Did,
            pericia.ProcessoId,
            pericia.VestigioId,
            pericia.Vestigio.RotuloEvidencia,
            pericia.Perito.Nome,
            pericia.Situacao == SituacaoPericia.DESIGNADA
                && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE);
}
