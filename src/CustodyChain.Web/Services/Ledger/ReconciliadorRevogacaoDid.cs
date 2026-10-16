using CustodyChain.Web.Application.RevogacaoDid;

namespace CustodyChain.Web.Services.Ledger;

/// <summary>Adaptador de hospedagem; a reconciliação e suas regras vivem na Application.</summary>
public sealed class ReconciliadorRevogacaoDid(IServiceScopeFactory scopeFactory, ILogger<ReconciliadorRevogacaoDid> logger)
    : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await ReconciliarPendenciasAsync(stoppingToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { logger.LogWarning(exception, "Falha ao reconciliar revogações DID pendentes."); }
        }
    }

    internal async Task ReconciliarPendenciasAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IReconciliarRevogacaoDid>();
        await useCase.ExecutarLoteAsync(cancellationToken);
    }
}
