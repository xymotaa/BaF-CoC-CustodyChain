using System.Text.Json;
using CustodyChain.Web.Data;
using CustodyChain.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustodyChain.Web.Services.Ledger;

/// <summary>Reenvia a mesma prova assinada; o commandId torna o reenvio idempotente no Fabric.</summary>
public sealed class ReconciliadorRevogacaoDid(IServiceScopeFactory scopeFactory, ILogger<ReconciliadorRevogacaoDid> logger)
    : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AtrasoTentativa = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ReconciliarPendenciasAsync(stoppingToken);
    }

    internal async Task ReconciliarPendenciasAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustodyChainDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<IServicoLedger>();
        var agora = DateTime.UtcNow;
        var pendencias = await db.SagasRevogacaoDid
            .Include(s => s.Interveniente)
            .Where(s => s.Estado != EstadoSagaRevogacaoDid.CONFIRMADA_NO_LEDGER
                && (s.ProximaTentativaEm == null || s.ProximaTentativaEm <= agora))
            .OrderBy(s => s.CriadaEm).Take(20).ToListAsync(cancellationToken);

        foreach (var saga in pendencias)
        {
            try
            {
                var command = JsonSerializer.Deserialize<JsonElement>(saga.CommandJson);
                await ledger.RevogarDidV2Async(saga.DidAlvo,
                    new RevogacaoDidV2Dto(command, saga.KeyId, saga.Signature), cancellationToken);
                saga.Estado = EstadoSagaRevogacaoDid.CONFIRMADA_NO_LEDGER;
                saga.ConfirmadaEm = agora;
                saga.ProximaTentativaEm = null;
                saga.Erro = null;
                saga.Interveniente.Situacao = SituacaoInterveniente.REVOGADO;
                saga.Interveniente.SituacaoIdentidadeLedger = SituacaoIdentidadeLedger.INATIVA;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                saga.Estado = EstadoSagaRevogacaoDid.FALHA_RECONCILIACAO;
                saga.Erro = exception.Message;
                saga.ProximaTentativaEm = agora.Add(AtrasoTentativa);
                logger.LogWarning(exception, "Falha ao reconciliar a revogação DID {CommandId}.", saga.CommandId);
            }
            finally
            {
                saga.Tentativas++;
                saga.UltimaTentativaEm = agora;
            }
        }
        if (pendencias.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }
}
