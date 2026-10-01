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

    public Task<AtorCadastroVestigio?> ObterAtorAtivoAsync(long intervenienteId, CancellationToken cancellationToken) =>
        db.Intervenientes
            .Where(i => i.Id == intervenienteId && i.Situacao == SituacaoInterveniente.ATIVO)
            .Select(i => new AtorCadastroVestigio(i.Id, i.Did))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<long> PersistirAsync(CadastroVestigioPendente cadastro, CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var vestigio = new Vestigio
            {
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
                HashSha256 = cadastro.PayloadHashSha256,
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

            db.Credenciais.Add(new Credencial
            {
                Tipo = TipoCredencial.COC,
                Identificador = cadastro.CredencialId,
                TitularId = cadastro.CriadorId,
                EmissorId = cadastro.CriadorId,
                ProcessoId = cadastro.ProcessoId,
                VestigioId = vestigio.Id,
                EmitidaEm = cadastro.CriadoEm,
                Situacao = SituacaoCredencial.PENDENTE,
            });

            db.RegistrosLedger.Add(new RegistroLedger
            {
                EntidadeOrigem = "VESTIGIO",
                RegistroOrigemId = vestigio.Id,
                VestigioId = vestigio.Id,
                Evento = "COLETA",
                PayloadJson = cadastro.PayloadJson,
                PayloadHashSha256 = cadastro.PayloadHashSha256,
                CredencialId = cadastro.CredencialId,
                DidResponsavel = cadastro.DidResponsavel,
                ChaveIdempotencia = cadastro.CredencialId,
                Estado = EstadoRegistroLedger.PENDENTE,
                Tentativas = 0,
                CriadoEm = cadastro.CriadoEm,
                ProximaTentativaEm = cadastro.CriadoEm,
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
