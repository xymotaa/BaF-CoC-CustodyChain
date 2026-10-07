using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.ProcessamentoPericial;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class ConsumoOuExaurimentoStore(CustodyChainDbContext db) : IConsumoOuExaurimentoStore
{
    public Task<ContextoConsumoOuExaurimento?> ObterContextoAsync(
        long periciaId,
        long peritoId,
        DateTime agora,
        CancellationToken cancellationToken) =>
        (from pericia in db.Pericias
         join perito in db.Intervenientes on peritoId equals perito.Id
         where pericia.Id == periciaId
             && pericia.PeritoId == peritoId
             && pericia.Situacao == SituacaoPericia.EM_EXECUCAO
             && perito.Situacao == SituacaoInterveniente.ATIVO
             && pericia.Credencial != null
             && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
             && (pericia.Credencial.ValidaAte == null || pericia.Credencial.ValidaAte > agora)
         select new ContextoConsumoOuExaurimento(
             pericia.Id,
             pericia.VestigioId,
             pericia.ProcessoId,
             pericia.Vestigio.RotuloEvidencia,
             perito.Did,
             pericia.Credencial!.Identificador))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task PersistirAsync(
        ConsumoOuExaurimentoPendente operacaoPendente,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var pericia = await db.Pericias
                .Include(p => p.Vestigio)
                .Include(p => p.Credencial)
                .SingleOrDefaultAsync(p => p.Id == operacaoPendente.PericiaId
                    && p.PeritoId == operacaoPendente.PeritoId
                    && p.Situacao == SituacaoPericia.EM_EXECUCAO, cancellationToken);
            var peritoAtivo = await db.Intervenientes.AnyAsync(i => i.Id == operacaoPendente.PeritoId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);

            if (pericia is null || !peritoAtivo || !PossuiCredencialValida(pericia, operacaoPendente.ExecutadoEm)
                || pericia.VestigioId != operacaoPendente.VestigioId)
                throw new ConflitoConsumoOuExaurimentoException(
                    "A perícia, o responsável ou o vestígio mudou enquanto a operação era preparada. Atualize a página e tente novamente.");

            var operacao = new OperacaoAmostra
            {
                PericiaId = pericia.Id,
                Tipo = TipoOperacao(operacaoPendente.Tipo),
                VestigioOrigemId = pericia.VestigioId,
                VestigioResultanteId = null,
                QuantidadeDescrita = operacaoPendente.QuantidadeDescrita,
                Justificativa = operacaoPendente.Justificativa,
                ExecutadoPorId = operacaoPendente.PeritoId,
                ExecutadoEm = operacaoPendente.ExecutadoEm,
            };
            db.OperacoesAmostra.Add(operacao);
            await db.SaveChangesAsync(cancellationToken);

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "OPERACAO_AMOSTRA",
                RegistroOrigemId = operacao.Id,
                VestigioId = pericia.VestigioId,
                Evento = operacaoPendente.Tipo,
                PayloadJson = operacaoPendente.OperacaoAssinadaJson,
                PayloadHashSha256 = operacaoPendente.OperacaoAssinadaHashSha256,
                DidResponsavel = operacaoPendente.DidResponsavel,
                ChaveIdempotencia = operacaoPendente.OperacaoAssinadaId,
                OperacaoAssinadaId = operacaoPendente.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = operacaoPendente.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = operacaoPendente.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = operacaoPendente.ExecutadoEm,
                AncoradoEm = operacaoPendente.ConfirmadoEm,
            });
            db.LogsAuditoria.Add(CriarLog(operacao.Id, operacaoPendente));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoConsumoOuExaurimentoException(
                "Não foi possível concluir a operação porque a credencial ou o evento já existe.");
        }
    }

    private static TipoOperacaoAmostra TipoOperacao(string tipo) => tipo switch
    {
        "CONSUMO" => TipoOperacaoAmostra.CONSUMO,
        "EXAURIMENTO" => TipoOperacaoAmostra.EXAURIMENTO,
        _ => throw new InvalidOperationException("Tipo de operação de amostra inválido."),
    };

    private static bool PossuiCredencialValida(Pericia pericia, DateTime agora) =>
        pericia.Credencial is not null
        && pericia.Credencial.Situacao == SituacaoCredencial.VIGENTE
        && pericia.Credencial.VestigioId == pericia.VestigioId
        && (pericia.Credencial.ValidaAte is null || pericia.Credencial.ValidaAte > agora);

    private LogAuditoria CriarLog(long operacaoId, ConsumoOuExaurimentoPendente operacao)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, operacao.Tipo, "OPERACAO_AMOSTRA", operacaoId,
            operacao.PeritoId, operacao.ExecutadoEm.Ticks);

        return new LogAuditoria
        {
            IntervenienteId = operacao.PeritoId,
            Acao = operacao.Tipo,
            Entidade = "OPERACAO_AMOSTRA",
            RegistroId = operacaoId,
            DataHora = operacao.ExecutadoEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
