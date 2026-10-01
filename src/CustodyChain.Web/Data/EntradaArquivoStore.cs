using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.Arquivo;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class EntradaArquivoStore(CustodyChainDbContext db) : IEntradaArquivoStore
{
    public Task<ContextoEntradaArquivo?> ObterContextoAsync(
        long vestigioId,
        long recebedorId,
        CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join recebedor in db.Intervenientes on recebedorId equals recebedor.Id
         where vestigio.Id == vestigioId
             && vestigio.Estado == EstadoVestigio.Recebido
             && vestigio.CustodianteAtualId == recebedorId
             && recebedor.Situacao == SituacaoInterveniente.ATIVO
         select new ContextoEntradaArquivo(vestigio.Id, vestigio.RotuloEvidencia, recebedor.Did))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task PersistirAsync(EntradaArquivoPendente entrada, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var vestigio = await db.Vestigios.SingleOrDefaultAsync(v => v.Id == entrada.VestigioId
                && v.Estado == EstadoVestigio.Recebido
                && v.CustodianteAtualId == entrada.RecebedorId, cancellationToken);
            var recebedorAtivo = await db.Intervenientes.AnyAsync(i => i.Id == entrada.RecebedorId
                && i.Situacao == SituacaoInterveniente.ATIVO, cancellationToken);

            if (vestigio is null || !recebedorAtivo)
                throw new ConflitoEntradaArquivoException(
                    "O vestígio ou o responsável mudou enquanto a entrada em guarda era preparada. Atualize a página e tente novamente.");

            db.Armazenamentos.Add(new Armazenamento
            {
                VestigioId = vestigio.Id,
                Central = entrada.Central,
                Posicao = entrada.Posicao,
                EntradaEm = entrada.EntradaEm,
                PrazoGuardaAte = entrada.PrazoGuardaAte,
                RecebidoPorId = entrada.RecebedorId,
                Situacao = SituacaoArmazenamento.GUARDADO,
            });

            vestigio.Estado = EstadoVestigio.Armazenado;
            vestigio.EtapaAtual = 9;
            vestigio.AtualizadoEm = entrada.EntradaEm;

            db.Credenciais.Add(new Credencial
            {
                Tipo = TipoCredencial.COC,
                Identificador = entrada.CredencialId,
                TitularId = entrada.RecebedorId,
                EmissorId = entrada.RecebedorId,
                VestigioId = vestigio.Id,
                EmitidaEm = entrada.EntradaEm,
                Situacao = SituacaoCredencial.PENDENTE,
            });
            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "VESTIGIO",
                RegistroOrigemId = vestigio.Id,
                VestigioId = vestigio.Id,
                Evento = "GUARDA",
                PayloadJson = entrada.PayloadJson,
                PayloadHashSha256 = entrada.PayloadHashSha256,
                CredencialId = entrada.CredencialId,
                DidResponsavel = entrada.DidResponsavel,
                ChaveIdempotencia = entrada.CredencialId,
                Estado = EstadoRegistroLedger.PENDENTE,
                Tentativas = 0,
                CriadoEm = entrada.EntradaEm,
                ProximaTentativaEm = entrada.EntradaEm,
            });
            db.LogsAuditoria.Add(CriarLogGuarda(vestigio.Id, entrada));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoEntradaArquivoException(
                "Não foi possível concluir a entrada em guarda porque uma credencial ou evento já existe.");
        }
    }

    private LogAuditoria CriarLogGuarda(long vestigioId, EntradaArquivoPendente entrada)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "GUARDA", "VESTIGIO", vestigioId,
            entrada.RecebedorId, entrada.EntradaEm.Ticks);

        return new LogAuditoria
        {
            IntervenienteId = entrada.RecebedorId,
            Acao = "GUARDA",
            Entidade = "VESTIGIO",
            RegistroId = vestigioId,
            DataHora = entrada.EntradaEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
