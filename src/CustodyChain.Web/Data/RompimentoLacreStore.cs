using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class RompimentoLacreStore(CustodyChainDbContext db) : IRompimentoLacreStore
{
    public async Task<ContextoRompimentoLacre?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken)
    {
        var contexto = await db.Pericias
            .Where(p => p.Id == periciaId && p.PeritoId == peritoId && p.Situacao == SituacaoPericia.RECEBIDA
                && p.Perito!.Situacao == SituacaoInterveniente.ATIVO)
            .Select(p => new
            {
                p.Id,
                p.VestigioId,
                p.ProcessoId,
                p.Vestigio.RotuloEvidencia,
                DidPerito = p.Perito!.Did,
                CredencialId = p.Credencial != null ? p.Credencial.Identificador : null,
                CredencialValida = p.Credencial != null
                    && p.Credencial.Situacao == SituacaoCredencial.VIGENTE
                    && p.Credencial.VestigioId == p.VestigioId
                    && (p.Credencial.ValidaAte == null || p.Credencial.ValidaAte > agora),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (contexto is null)
            return null;

        var lacre = await db.Lacres
            .Where(l => l.VestigioId == contexto.VestigioId && l.Situacao == SituacaoLacre.INTACTO)
            .OrderByDescending(l => l.AplicadoEm)
            .Select(l => new { l.Id, l.Numero })
            .FirstOrDefaultAsync(cancellationToken);

        return new ContextoRompimentoLacre(
            contexto.Id,
            contexto.VestigioId,
            contexto.RotuloEvidencia,
            contexto.DidPerito,
            contexto.CredencialValida,
            contexto.ProcessoId,
            contexto.CredencialId,
            lacre?.Id,
            lacre?.Numero);
    }

    public async Task PersistirAsync(RompimentoLacrePendente rompimento, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await db.Pericias
                .Include(p => p.Vestigio)
                .Include(p => p.Credencial)
                .SingleOrDefaultAsync(p => p.Id == rompimento.PericiaId
                    && p.PeritoId == rompimento.PeritoId
                    && p.Situacao == SituacaoPericia.RECEBIDA, cancellationToken);
            var peritoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == rompimento.PeritoId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);

            if (pericia is null || !peritoAtivo || !PossuiCredencialValida(pericia, rompimento.RompidoEm))
                throw new ConflitoRompimentoLacreException(
                    "A perícia ou credencial mudou enquanto o lacre era rompido. Atualize a página e tente novamente.");

            var lacre = await db.Lacres.SingleOrDefaultAsync(l => l.Id == rompimento.LacreId
                && l.VestigioId == pericia.VestigioId
                && l.Situacao == SituacaoLacre.INTACTO, cancellationToken);
            if (lacre is null || lacre.Numero != rompimento.NumeroLacre)
                throw new ConflitoRompimentoLacreException(
                    "O lacre esperado mudou enquanto era rompido. Atualize a página e tente novamente.");

            lacre.Situacao = SituacaoLacre.ROMPIDO;
            lacre.RompidoPorId = rompimento.PeritoId;
            lacre.RompidoEm = rompimento.RompidoEm;
            lacre.JustificativaRompimento = rompimento.Justificativa;

            pericia.Situacao = SituacaoPericia.EM_EXECUCAO;
            pericia.Vestigio.Estado = EstadoVestigio.EmPericia;
            pericia.Vestigio.AtualizadoEm = rompimento.RompidoEm;

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "LACRE",
                RegistroOrigemId = lacre.Id,
                VestigioId = pericia.VestigioId,
                Evento = "LACRE_ROMPER",
                PayloadJson = rompimento.OperacaoAssinadaJson,
                PayloadHashSha256 = rompimento.OperacaoAssinadaHashSha256,
                DidResponsavel = rompimento.DidResponsavel,
                ChaveIdempotencia = rompimento.OperacaoAssinadaId,
                OperacaoAssinadaId = rompimento.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = rompimento.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = rompimento.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = rompimento.RompidoEm,
                AncoradoEm = rompimento.ConfirmadoEm,
            });
            db.LogsAuditoria.Add(CriarLogRompimento(lacre.Id, rompimento));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoRompimentoLacreException(
                "Não foi possível concluir o rompimento porque uma credencial ou evento já existe.");
        }
    }

    private static bool PossuiCredencialValida(Pericia pericia, DateTime agora) =>
        pericia.Credencial is not null
        && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
        && pericia.Credencial.VestigioId == pericia.VestigioId
        && (pericia.Credencial.ValidaAte is null || pericia.Credencial.ValidaAte > agora);

    private LogAuditoria CriarLogRompimento(long lacreId, RompimentoLacrePendente rompimento)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "ROMPIMENTO", "LACRE", lacreId,
            rompimento.PeritoId, rompimento.RompidoEm.Ticks);

        return new LogAuditoria
        {
            IntervenienteId = rompimento.PeritoId,
            Acao = "ROMPIMENTO",
            Entidade = "LACRE",
            RegistroId = lacreId,
            DataHora = rompimento.RompidoEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
