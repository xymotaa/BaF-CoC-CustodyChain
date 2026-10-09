using System.Security.Cryptography;
using System.Text;
using CustodyChain.Web.Application.CadastroVestigio;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Data;

public sealed class CadastroVestigioStore(CustodyChainDbContext db) : ICadastroVestigioStore
{
    public Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken) =>
        db.Vestigios.AnyAsync(v => v.RotuloEvidencia == rotuloEvidencia, cancellationToken);

    public Task<bool> NumeroLacreExisteAsync(string numeroLacre, CancellationToken cancellationToken) =>
        db.Lacres.AnyAsync(l => l.Numero == numeroLacre, cancellationToken);

    public Task<ProcessoCadastroVestigio?> ObterProcessoAtivoAsync(long processoId, CancellationToken cancellationToken) =>
        db.Processos
            .Where(p => p.Id == processoId && p.Situacao == SituacaoProcesso.ATIVO)
            .Select(p => new ProcessoCadastroVestigio(p.Id, p.Numero))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> TipoVestigioExisteAsync(short tipoVestigioId, CancellationToken cancellationToken) =>
        db.TiposVestigio.AnyAsync(t => t.Id == tipoVestigioId, cancellationToken);

    public Task<AtorCadastroVestigio?> ObterAtorAtivoAsync(
        long intervenienteId,
        long processoId,
        DateTime agora,
        CancellationToken cancellationToken) =>
        (from interveniente in db.Intervenientes.AptosParaOperacoesLedger()
         join perfil in db.Perfis on interveniente.PerfilId equals perfil.Id
         join credencial in db.Credenciais on interveniente.Id equals credencial.TitularId
         where interveniente.Id == intervenienteId
               && interveniente.Situacao == SituacaoInterveniente.ATIVO
               && perfil.Codigo == "COLETOR"
               && credencial.Tipo == TipoCredencial.PERMISSAO
               && credencial.ProcessoId == processoId
               && credencial.VestigioId == null
               && credencial.Situacao == SituacaoCredencial.VIGENTE
               && (credencial.ValidaAte == null || credencial.ValidaAte > agora)
         orderby credencial.EmitidaEm descending
         select new AtorCadastroVestigio(interveniente.Id, interveniente.Did, credencial.Identificador))
        .FirstOrDefaultAsync(cancellationToken);

    public async Task<long> PersistirAsync(CadastroVestigioPendente cadastro, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var vestigio = new Vestigio
            {
                AssetRef = cadastro.AssetRef,
                RotuloEvidencia = cadastro.RotuloEvidencia,
                RotuloConjunto = cadastro.RotuloConjunto,
                NumeroEvidencia = cadastro.NumeroEvidencia,
                ProcessoId = cadastro.ProcessoId,
                TipoVestigioId = cadastro.TipoVestigioId,
                Descricao = cadastro.Descricao,
                CriadorId = cadastro.CriadorId,
                CustodianteAtualId = cadastro.CriadorId,
                LocalColeta = cadastro.LocalColeta,
                DataHoraColeta = cadastro.DataHoraColeta,
                MetodoColeta = cadastro.MetodoColeta,
                HouveIntercorrencia = cadastro.HouveIntercorrencia,
                DescricaoIntercorrencia = cadastro.DescricaoIntercorrencia,
                HashSha256 = cadastro.OperacaoAssinadaHashSha256,
                EtapaAtual = 4,
                FaseAtual = FaseVestigio.EXTERNA,
                Estado = EstadoVestigio.Coletado,
                CriadoEm = cadastro.CriadoEm,
            };

            db.Vestigios.Add(vestigio);
            await db.SaveChangesAsync(cancellationToken);

            db.Lacres.Add(new Lacre
            {
                VestigioId = vestigio.Id,
                Numero = cadastro.NumeroLacre,
                Situacao = SituacaoLacre.INTACTO,
                AplicadoPorId = cadastro.CriadorId,
                AplicadoEm = cadastro.CriadoEm,
            });

            if (cadastro.Integridade is not null)
            {
                db.Anexos.Add(new Anexo
                {
                    VestigioId = vestigio.Id,
                    Tipo = TipoAnexo.DOCUMENTO,
                    NomeArquivo = cadastro.Integridade.FileName,
                    CaminhoRelativo = cadastro.Integridade.ContentCid,
                    TamanhoBytes = cadastro.Integridade.ByteLength,
                    HashSha256 = cadastro.Integridade.ContentHashSha256,
                    Algoritmo = cadastro.Integridade.Algorithm,
                    EnviadoPorId = cadastro.CriadorId,
                    EnviadoEm = cadastro.ConfirmadoEm,
                });
            }

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "VESTIGIO",
                RegistroOrigemId = vestigio.Id,
                VestigioId = vestigio.Id,
                Evento = "COLETA_REGISTRAR",
                PayloadJson = cadastro.OperacaoAssinadaJson,
                PayloadHashSha256 = cadastro.OperacaoAssinadaHashSha256,
                DidResponsavel = cadastro.DidResponsavel,
                ChaveIdempotencia = cadastro.OperacaoAssinadaId,
                OperacaoAssinadaId = cadastro.OperacaoAssinadaId,
                VersaoOperacaoAssinada = 1,
                OperacaoAssinadaJson = cadastro.OperacaoAssinadaJson,
                OperacaoAssinadaHashSha256 = cadastro.OperacaoAssinadaHashSha256,
                Estado = EstadoRegistroLedger.ANCORADO,
                Tentativas = 1,
                CriadoEm = cadastro.CriadoEm,
                AncoradoEm = cadastro.ConfirmadoEm,
            });

            db.LogsAuditoria.Add(CriarLogCadastro(vestigio.Id, cadastro));

            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return vestigio.Id;
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(cancellationToken);
            throw new ConflitoCadastroVestigioException(
                "Não foi possível concluir o cadastro porque um rótulo, lacre ou credencial já existe.",
                null);
        }
    }

    private LogAuditoria CriarLogCadastro(long vestigioId, CadastroVestigioPendente cadastro)
    {
        var hashAnterior = db.LogsAuditoria
            .OrderByDescending(log => log.Id)
            .Select(log => log.HashRegistro)
            .FirstOrDefault() ?? new string('0', 64);
        var conteudo = string.Join('|', hashAnterior, "CADASTRO", "VESTIGIO", vestigioId, cadastro.CriadorId, cadastro.CriadoEm.Ticks);
        var hashRegistro = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));

        return new LogAuditoria
        {
            IntervenienteId = cadastro.CriadorId,
            Acao = "CADASTRO",
            Entidade = "VESTIGIO",
            RegistroId = vestigioId,
            DataHora = cadastro.CriadoEm,
            HashAnterior = hashAnterior,
            HashRegistro = hashRegistro,
        };
    }
}
