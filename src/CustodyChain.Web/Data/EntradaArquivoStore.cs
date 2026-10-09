using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.Arquivo;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class EntradaArquivoStore(CustodyChainDbContext db) : IEntradaArquivoStore
{
    public Task<ContextoEntradaArquivo?> ObterContextoAsync(long vestigioId, long recebedorId, CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join recebedor in db.Intervenientes.AptosParaOperacoesLedger() on recebedorId equals recebedor.Id
         join perfil in db.Perfis on recebedor.PerfilId equals perfil.Id
         join credencial in db.Credenciais on recebedor.Id equals credencial.TitularId
         join movimentacao in db.Movimentacoes on vestigio.Id equals movimentacao.VestigioId
         join recebimento in db.RegistrosLedger on movimentacao.Id equals recebimento.RegistroOrigemId
         where vestigio.Id == vestigioId && vestigio.Estado == EstadoVestigio.Recebido
               && vestigio.CustodianteAtualId == recebedorId && vestigio.AssetRef != null
               && recebedor.Situacao == SituacaoInterveniente.ATIVO && perfil.Codigo == "CUSTODIA"
               && credencial.Tipo == TipoCredencial.PERMISSAO && credencial.Situacao == SituacaoCredencial.VIGENTE
               && credencial.ProcessoId == vestigio.ProcessoId && credencial.VestigioId == vestigio.Id
               && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
               && movimentacao.DestinoId == recebedorId && movimentacao.Situacao == SituacaoMovimentacao.ACEITA
               && recebimento.Evento == "REMESSA_RECEBER" && recebimento.Estado == EstadoRegistroLedger.ANCORADO
               && recebimento.OperacaoAssinadaId != null
         orderby movimentacao.DataHoraChegada descending, credencial.EmitidaEm descending
         select new ContextoEntradaArquivo(vestigio.Id, vestigio.ProcessoId, vestigio.AssetRef!, vestigio.RotuloEvidencia,
             recebedor.Did, credencial.Identificador, recebimento.OperacaoAssinadaId!))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task PersistirAsync(EntradaArquivoConfirmada entrada, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var vestigio = await db.Vestigios.SingleOrDefaultAsync(v => v.Id == entrada.VestigioId
                && v.Estado == EstadoVestigio.Recebido && v.CustodianteAtualId == entrada.RecebedorId && v.AssetRef != null, cancellationToken);
            if (vestigio is null || !await PossuiVcCustodiaVigenteAsync(entrada.VestigioId, entrada.RecebedorId, cancellationToken)
                || !await PossuiRecebimentoAssinadoAsync(entrada, cancellationToken))
                throw new ConflitoEntradaArquivoException("O vestígio, o responsável ou a permissão mudou enquanto a guarda era confirmada. Atualize a página e tente novamente.");

            db.Armazenamentos.Add(new Armazenamento
            {
                VestigioId = vestigio.Id, Central = entrada.Central, Posicao = entrada.Posicao, EntradaEm = entrada.EntradaEm,
                PrazoGuardaAte = entrada.PrazoGuardaAte, RecebidoPorId = entrada.RecebedorId, Situacao = SituacaoArmazenamento.GUARDADO
            });
            vestigio.Estado = EstadoVestigio.Armazenado;
            vestigio.EtapaAtual = 9;
            vestigio.AtualizadoEm = entrada.EntradaEm;
            db.RegistrosLedger.Add(CriarRegistroLedger(vestigio.Id, entrada));
            db.LogsAuditoria.Add(CriarLogGuarda(vestigio.Id, entrada));
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoEntradaArquivoException("Não foi possível concluir a guarda porque o armazenamento ou o registro de autorização já existe.");
        }
    }

    private Task<bool> PossuiVcCustodiaVigenteAsync(long vestigioId, long recebedorId, CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join recebedor in db.Intervenientes.AptosParaOperacoesLedger() on recebedorId equals recebedor.Id
         join perfil in db.Perfis on recebedor.PerfilId equals perfil.Id
         join credencial in db.Credenciais on recebedor.Id equals credencial.TitularId
         where vestigio.Id == vestigioId && recebedor.Situacao == SituacaoInterveniente.ATIVO && perfil.Codigo == "CUSTODIA"
               && credencial.Tipo == TipoCredencial.PERMISSAO && credencial.Situacao == SituacaoCredencial.VIGENTE
               && credencial.ProcessoId == vestigio.ProcessoId && credencial.VestigioId == vestigio.Id
               && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
         select credencial.Id).AnyAsync(cancellationToken);

    private Task<bool> PossuiRecebimentoAssinadoAsync(EntradaArquivoConfirmada entrada, CancellationToken cancellationToken) =>
        (from movimentacao in db.Movimentacoes
         join recebimento in db.RegistrosLedger on movimentacao.Id equals recebimento.RegistroOrigemId
         where movimentacao.VestigioId == entrada.VestigioId && movimentacao.DestinoId == entrada.RecebedorId
               && movimentacao.Situacao == SituacaoMovimentacao.ACEITA && recebimento.Evento == "REMESSA_RECEBER"
               && recebimento.Estado == EstadoRegistroLedger.ANCORADO
               && recebimento.OperacaoAssinadaId == entrada.RecebimentoOperationId
         select recebimento.Id).AnyAsync(cancellationToken);

    private static RegistroLedger CriarRegistroLedger(long vestigioId, EntradaArquivoConfirmada entrada) => new()
    {
        EntidadeOrigem = "VESTIGIO", RegistroOrigemId = vestigioId, VestigioId = vestigioId, Evento = "GUARDA_REGISTRAR",
        PayloadJson = entrada.OperacaoAssinadaJson, PayloadHashSha256 = entrada.OperacaoAssinadaHashSha256,
        DidResponsavel = entrada.DidResponsavel, ChaveIdempotencia = entrada.OperacaoAssinadaId,
        OperacaoAssinadaId = entrada.OperacaoAssinadaId, VersaoOperacaoAssinada = 1,
        OperacaoAssinadaJson = entrada.OperacaoAssinadaJson, OperacaoAssinadaHashSha256 = entrada.OperacaoAssinadaHashSha256,
        Estado = EstadoRegistroLedger.ANCORADO, Tentativas = 1, CriadoEm = entrada.EntradaEm, AncoradoEm = entrada.EntradaEm
    };

    private LogAuditoria CriarLogGuarda(long vestigioId, EntradaArquivoConfirmada entrada)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id).Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "GUARDA_REGISTRAR", "VESTIGIO", vestigioId, entrada.RecebedorId, entrada.EntradaEm.Ticks);
        return new LogAuditoria
        {
            IntervenienteId = entrada.RecebedorId, Acao = "GUARDA_REGISTRAR", Entidade = "VESTIGIO", RegistroId = vestigioId,
            DataHora = entrada.EntradaEm, HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)))
        };
    }
}
