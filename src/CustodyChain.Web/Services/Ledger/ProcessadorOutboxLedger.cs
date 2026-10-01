using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Services.Ledger;

public sealed class ProcessadorOutboxLedger(
    CustodyChainDbContext db,
    IServicoLedger ledger,
    ILogger<ProcessadorOutboxLedger> logger) : IProcessadorOutboxLedger
{
    private const int TamanhoLote = 10;
    private const byte MaximoTentativas = 10;
    private static readonly TimeSpan DuracaoReserva = TimeSpan.FromMinutes(5);

    public async Task<int> ProcessarLoteAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTime.UtcNow;
        await LiberarRegistrosAbandonadosAsync(agora, cancellationToken);

        var ids = await db.RegistrosLedger
            .AsNoTracking()
            .Where(registro => registro.Estado == EstadoRegistroLedger.PENDENTE
                && registro.ChaveIdempotencia != null
                && registro.CredencialId != null
                && registro.DidResponsavel != null
                && registro.PayloadHashSha256 != null
                && (registro.ProximaTentativaEm == null || registro.ProximaTentativaEm <= agora))
            .OrderBy(registro => registro.CriadoEm)
            .Select(registro => registro.Id)
            .Take(TamanhoLote)
            .ToListAsync(cancellationToken);

        var processados = 0;
        foreach (var registroId in ids)
        {
            if (await ProcessarRegistroAsync(registroId, cancellationToken))
            {
                processados++;
            }
        }

        return processados;
    }

    private async Task<bool> ProcessarRegistroAsync(long registroId, CancellationToken cancellationToken)
    {
        var registro = await ReservarAsync(registroId, cancellationToken);
        if (registro is null)
        {
            return false;
        }

        try
        {
            await ledger.EmitirCredencialCoCAsync(
                new CredencialCoCDto(
                    registro.VestigioId!.Value.ToString(),
                    registro.Evento,
                    registro.DidResponsavel!,
                    registro.PayloadHashSha256!,
                    registro.CredencialId),
                cancellationToken);

            await MarcarComoAncoradoAsync(registro.Id, registro.CredencialId!, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao publicar o registro {RegistroLedgerId} no ledger.", registro.Id);
            await RegistrarFalhaAsync(registro.Id, cancellationToken);
        }

        return true;
    }

    private async Task<RegistroLedger?> ReservarAsync(long registroId, CancellationToken cancellationToken)
    {
        var registro = await db.RegistrosLedger.SingleOrDefaultAsync(r => r.Id == registroId, cancellationToken);
        if (registro is null || registro.Estado != EstadoRegistroLedger.PENDENTE)
        {
            return null;
        }

        registro.Estado = EstadoRegistroLedger.PROCESSANDO;
        registro.ProcessandoEm = DateTime.UtcNow;
        registro.ProximaTentativaEm = null;
        registro.Tentativas++;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return registro;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private async Task MarcarComoAncoradoAsync(long registroId, string credencialId, CancellationToken cancellationToken)
    {
        var registro = await db.RegistrosLedger.SingleAsync(r => r.Id == registroId, cancellationToken);
        registro.Estado = EstadoRegistroLedger.ANCORADO;
        registro.AncoradoEm = DateTime.UtcNow;
        registro.ProcessandoEm = null;
        registro.Erro = null;

        var credencial = await db.Credenciais.SingleOrDefaultAsync(c => c.Identificador == credencialId, cancellationToken);
        if (credencial is not null && credencial.Situacao == SituacaoCredencial.PENDENTE)
        {
            credencial.Situacao = SituacaoCredencial.VIGENTE;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RegistrarFalhaAsync(long registroId, CancellationToken cancellationToken)
    {
        var registro = await db.RegistrosLedger.SingleAsync(r => r.Id == registroId, cancellationToken);
        registro.ProcessandoEm = null;
        registro.Erro = "Falha temporária ao publicar no ledger.";

        if (registro.Tentativas >= MaximoTentativas)
        {
            registro.Estado = EstadoRegistroLedger.FALHA;
            registro.ProximaTentativaEm = null;
        }
        else
        {
            registro.Estado = EstadoRegistroLedger.PENDENTE;
            registro.ProximaTentativaEm = DateTime.UtcNow.Add(CalcularAtraso(registro.Tentativas));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task LiberarRegistrosAbandonadosAsync(DateTime agora, CancellationToken cancellationToken)
    {
        var limite = agora.Subtract(DuracaoReserva);
        var abandonados = await db.RegistrosLedger
            .Where(registro => registro.Estado == EstadoRegistroLedger.PROCESSANDO
                && registro.ProcessandoEm != null
                && registro.ProcessandoEm < limite)
            .ToListAsync(cancellationToken);

        foreach (var registro in abandonados)
        {
            registro.Estado = EstadoRegistroLedger.PENDENTE;
            registro.ProcessandoEm = null;
            registro.ProximaTentativaEm = agora;
            registro.Erro = "Reserva de publicação expirou; o registro será reprocessado.";
        }

        if (abandonados.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static TimeSpan CalcularAtraso(byte tentativas)
    {
        var segundos = Math.Min(300, (int)Math.Pow(2, tentativas));
        return TimeSpan.FromSeconds(segundos);
    }
}
