namespace CustodyChain.Web.Services.Ledger;

public sealed class PublicadorOutboxLedgerService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<PublicadorOutboxLedgerService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var processador = scope.ServiceProvider.GetRequiredService<IProcessadorOutboxLedger>();
                await processador.ProcessarLoteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha inesperada no publicador da outbox do ledger.");
            }

            await Task.Delay(Intervalo, stoppingToken);
        }
    }
}
