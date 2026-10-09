using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.DestinacaoFinal;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class DestinacaoFinalStore(CustodyChainDbContext db) : IDestinacaoFinalStore
{
    public Task<ContextoSolicitacaoDestinacao?> ObterContextoSolicitacaoAsync(
        long vestigioId,
        long solicitanteId,
        CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join solicitante in db.Intervenientes.AptosParaOperacoesLedger() on solicitanteId equals solicitante.Id
         join perfil in db.Perfis on solicitante.PerfilId equals perfil.Id
         join credencial in db.Credenciais on solicitante.Id equals credencial.TitularId
         join guarda in db.RegistrosLedger on vestigio.Id equals guarda.VestigioId
         where vestigio.Id == vestigioId
             && (vestigio.Estado == EstadoVestigio.Armazenado || vestigio.Estado == EstadoVestigio.Periciado)
             && solicitante.Situacao == SituacaoInterveniente.ATIVO
             && perfil.Codigo == "CUSTODIA"
             && vestigio.CustodianteAtualId == solicitanteId
             && vestigio.AssetRef != null
             && credencial.Tipo == TipoCredencial.PERMISSAO
             && credencial.Situacao == SituacaoCredencial.VIGENTE
             && credencial.ProcessoId == vestigio.ProcessoId
             && credencial.VestigioId == vestigio.Id
             && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
             && guarda.Evento == "GUARDA_REGISTRAR"
             && guarda.Estado == EstadoRegistroLedger.ANCORADO
             && guarda.OperacaoAssinadaId != null
             && guarda.DidResponsavel == solicitante.Did
         orderby guarda.AncoradoEm descending, credencial.EmitidaEm descending
         select new ContextoSolicitacaoDestinacao(
             vestigio.Id,
             vestigio.ProcessoId,
             vestigio.AssetRef!,
             vestigio.RotuloEvidencia,
             solicitante.Did,
             credencial.Identificador,
             guarda.OperacaoAssinadaId!))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task PersistirSolicitacaoAsync(
        SolicitacaoDestinacaoPendente solicitacao,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var vestigioElegivel = await db.Vestigios.AnyAsync(v => v.Id == solicitacao.VestigioId
                && (v.Estado == EstadoVestigio.Armazenado || v.Estado == EstadoVestigio.Periciado), cancellationToken);
            var solicitanteAtivo = await db.Intervenientes.AptosParaOperacoesLedger()
                .AnyAsync(i => i.Id == solicitacao.SolicitanteId, cancellationToken);
            var solicitanteAutorizado = await PossuiPermissaoCustodiaEGuardaAsync(solicitacao, cancellationToken);
            if (!vestigioElegivel || !solicitanteAtivo || !solicitanteAutorizado)
                throw new ConflitoDestinacaoFinalException(
                    "O vestígio ou o solicitante mudou enquanto a destinação era preparada. Atualize a página e tente novamente.");

            var anexo = new Anexo
            {
                VestigioId = solicitacao.VestigioId,
                Tipo = TipoAnexo.AUTORIZACAO,
                NomeArquivo = solicitacao.NomeArquivoAutorizacao,
                CaminhoRelativo = solicitacao.CidAutorizacao,
                TamanhoBytes = solicitacao.TamanhoBytesAutorizacao,
                HashSha256 = solicitacao.HashAutorizacao,
                Algoritmo = "SHA-256",
                EnviadoPorId = solicitacao.SolicitanteId,
                EnviadoEm = solicitacao.SolicitadoEm,
            };
            db.Anexos.Add(anexo);
            await db.SaveChangesAsync(cancellationToken);

            var descarte = new Descarte
            {
                VestigioId = solicitacao.VestigioId,
                Tipo = TipoDescarte(solicitacao.Tipo),
                AutorizacaoAnexoId = anexo.Id,
                DidMagistrado = solicitacao.DidMagistrado,
                SolicitadoPorId = solicitacao.SolicitanteId,
                Observacao = solicitacao.Observacao,
            };
            db.Descartes.Add(descarte);
            await db.SaveChangesAsync(cancellationToken);

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "DESCARTE",
                RegistroOrigemId = descarte.Id,
                VestigioId = solicitacao.VestigioId,
                Evento = "DESTINACAO_SOLICITAR",
                PayloadJson = solicitacao.OperacaoAssinadaJson,
                PayloadHashSha256 = solicitacao.OperacaoAssinadaHashSha256,
                CredencialId = solicitacao.CredencialId,
                DidResponsavel = solicitacao.DidResponsavel,
                ChaveIdempotencia = solicitacao.OperacaoAssinadaId,
                OperacaoAssinadaId = solicitacao.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = solicitacao.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = solicitacao.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = solicitacao.SolicitadoEm,
                AncoradoEm = solicitacao.SolicitadoEm,
            });

            db.LogsAuditoria.Add(CriarLog(
                "SOLICITACAO_DESTINACAO",
                descarte.Id,
                solicitacao.SolicitanteId,
                solicitacao.SolicitadoEm));
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoDestinacaoFinalException(
                "Não foi possível registrar a solicitação de destinação final.");
        }
    }

    public Task<ContextoAprovacaoDestinacao?> ObterContextoAprovacaoAsync(
        long descarteId,
        long aprovadorId,
        CancellationToken cancellationToken) =>
        (from descarte in db.Descartes
         join aprovador in db.Intervenientes.AptosParaOperacoesLedger() on aprovadorId equals aprovador.Id
         join perfil in db.Perfis on aprovador.PerfilId equals perfil.Id
         join solicitacao in db.RegistrosLedger on descarte.Id equals solicitacao.RegistroOrigemId
         where descarte.Id == descarteId
             && descarte.AprovadoPorId == null
             && descarte.ExecutadoEm == null
             && descarte.SolicitadoPorId != aprovadorId
             && aprovador.Situacao == SituacaoInterveniente.ATIVO
             && perfil.Codigo == "ADMIN"
             && solicitacao.EntidadeOrigem == "DESCARTE"
             && solicitacao.Evento == "DESTINACAO_SOLICITAR"
             && solicitacao.Estado == EstadoRegistroLedger.ANCORADO
             && solicitacao.OperacaoAssinadaId != null
             && descarte.AutorizacaoAnexo.CaminhoRelativo != null
             && descarte.AutorizacaoAnexo.HashSha256 != null
         select new ContextoAprovacaoDestinacao(
             descarte.Id,
             descarte.VestigioId,
             descarte.Vestigio.ProcessoId,
             descarte.Vestigio.AssetRef!,
             descarte.Vestigio.RotuloEvidencia,
             descarte.Tipo.ToString(),
             descarte.AutorizacaoAnexo.CaminhoRelativo!,
             descarte.AutorizacaoAnexo.HashSha256!,
             solicitacao.OperacaoAssinadaId!,
             solicitacao.DidResponsavel!,
             aprovador.Did))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task PersistirAprovacaoAsync(
        AprovacaoDestinacaoPendente aprovacao,
        CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var descarte = await db.Descartes
                .Include(d => d.Vestigio)
                .SingleOrDefaultAsync(d => d.Id == aprovacao.DescarteId
                    && d.AprovadoPorId == null
                    && d.ExecutadoEm == null
                    && d.SolicitadoPorId != aprovacao.AprovadorId, cancellationToken);
            var aprovadorAutorizado = await PossuiAdministradorERequisicaoAncoradaAsync(aprovacao, cancellationToken);

            if (descarte is null || !aprovadorAutorizado || descarte.VestigioId != aprovacao.VestigioId
                || descarte.Tipo.ToString() != aprovacao.Tipo
                || (descarte.Vestigio.Estado != EstadoVestigio.Armazenado
                    && descarte.Vestigio.Estado != EstadoVestigio.Periciado))
                throw new ConflitoDestinacaoFinalException(
                    "A destinação, o aprovador ou o vestígio mudou enquanto a aprovação era preparada. Atualize a página e tente novamente.");

            descarte.AprovadoPorId = aprovacao.AprovadorId;
            descarte.ExecutadoEm = aprovacao.ExecutadoEm;
            descarte.Vestigio.Estado = EstadoVestigio.Descartado;
            descarte.Vestigio.EtapaAtual = 10;
            descarte.Vestigio.AtualizadoEm = aprovacao.ExecutadoEm;

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "DESCARTE",
                RegistroOrigemId = descarte.Id,
                VestigioId = aprovacao.VestigioId,
                Evento = "DESTINACAO_APROVAR",
                PayloadJson = aprovacao.OperacaoAssinadaJson,
                PayloadHashSha256 = aprovacao.OperacaoAssinadaHashSha256,
                DidResponsavel = aprovacao.DidResponsavel,
                ChaveIdempotencia = aprovacao.OperacaoAssinadaId,
                OperacaoAssinadaId = aprovacao.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = aprovacao.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = aprovacao.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = aprovacao.ExecutadoEm,
                AncoradoEm = aprovacao.ExecutadoEm,
            });
            db.LogsAuditoria.Add(CriarLog(
                "DESTINACAO_APROVAR",
                descarte.Id,
                aprovacao.AprovadorId,
                aprovacao.ExecutadoEm));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoDestinacaoFinalException(
                "Não foi possível concluir a destinação porque a aprovação assinada já existe.");
        }
    }

    private static TipoDescarte TipoDescarte(string tipo) => tipo switch
    {
        "DESCARTE" => Models.Entities.TipoDescarte.DESCARTE,
        "RESTITUICAO" => Models.Entities.TipoDescarte.RESTITUICAO,
        _ => throw new InvalidOperationException("Tipo de destinação inválido."),
    };

    private Task<bool> PossuiPermissaoCustodiaEGuardaAsync(
        SolicitacaoDestinacaoPendente solicitacao,
        CancellationToken cancellationToken) =>
        (from vestigio in db.Vestigios
         join solicitante in db.Intervenientes.AptosParaOperacoesLedger() on solicitacao.SolicitanteId equals solicitante.Id
         join perfil in db.Perfis on solicitante.PerfilId equals perfil.Id
         join credencial in db.Credenciais on solicitante.Id equals credencial.TitularId
         join guarda in db.RegistrosLedger on vestigio.Id equals guarda.VestigioId
         where vestigio.Id == solicitacao.VestigioId
             && vestigio.CustodianteAtualId == solicitacao.SolicitanteId
             && vestigio.AssetRef != null
             && solicitante.Situacao == SituacaoInterveniente.ATIVO
             && perfil.Codigo == "CUSTODIA"
             && credencial.Tipo == TipoCredencial.PERMISSAO
             && credencial.Situacao == SituacaoCredencial.VIGENTE
             && credencial.ProcessoId == vestigio.ProcessoId
             && credencial.VestigioId == vestigio.Id
             && credencial.Identificador == solicitacao.CredencialId
             && (credencial.ValidaAte == null || credencial.ValidaAte > DateTime.UtcNow)
             && guarda.Evento == "GUARDA_REGISTRAR"
             && guarda.Estado == EstadoRegistroLedger.ANCORADO
             && guarda.OperacaoAssinadaId == solicitacao.GuardaOperationId
             && guarda.DidResponsavel == solicitacao.DidResponsavel
         select guarda.Id).AnyAsync(cancellationToken);

    private Task<bool> PossuiAdministradorERequisicaoAncoradaAsync(
        AprovacaoDestinacaoPendente aprovacao,
        CancellationToken cancellationToken) =>
        (from descarte in db.Descartes
         join aprovador in db.Intervenientes.AptosParaOperacoesLedger() on aprovacao.AprovadorId equals aprovador.Id
         join perfil in db.Perfis on aprovador.PerfilId equals perfil.Id
         join solicitacao in db.RegistrosLedger on descarte.Id equals solicitacao.RegistroOrigemId
         where descarte.Id == aprovacao.DescarteId
             && descarte.SolicitadoPorId != aprovacao.AprovadorId
             && aprovador.Situacao == SituacaoInterveniente.ATIVO
             && perfil.Codigo == "ADMIN"
             && solicitacao.EntidadeOrigem == "DESCARTE"
             && solicitacao.Evento == "DESTINACAO_SOLICITAR"
             && solicitacao.Estado == EstadoRegistroLedger.ANCORADO
             && solicitacao.OperacaoAssinadaId == aprovacao.SolicitacaoOperationId
         select solicitacao.Id).AnyAsync(cancellationToken);

    private LogAuditoria CriarLog(string acao, long descarteId, long responsavelId, DateTime ocorridoEm)
    {
        var hashAnterior = db.LogsAuditoria.OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro).FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, acao, "DESCARTE", descarteId, responsavelId, ocorridoEm.Ticks);

        return new LogAuditoria
        {
            IntervenienteId = responsavelId,
            Acao = acao,
            Entidade = "DESCARTE",
            RegistroId = descarteId,
            DataHora = ocorridoEm,
            HashAnterior = hashAnterior,
            HashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo))),
        };
    }
}
