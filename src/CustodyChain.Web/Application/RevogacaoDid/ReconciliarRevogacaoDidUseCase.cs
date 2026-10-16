namespace CustodyChain.Web.Application.RevogacaoDid;

public sealed class ReconciliarRevogacaoDidUseCase(IRevogacaoDidStore store, IRevogacaoDidLedger ledger, CustodyChain.Web.Application.Common.IClock clock) : IReconciliarRevogacaoDid
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AtrasoTentativa = TimeSpan.FromMinutes(2);

    public async Task<int> ExecutarLoteAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var pendencias = await store.AssumirPendenciasAsync(agora, Lease, 20, cancellationToken);
        foreach (var saga in pendencias)
        {
            try
            {
                using var documento = System.Text.Json.JsonDocument.Parse(saga.CommandJson);
                await ledger.RevogarAsync(saga.DidAlvo, new ProvaRevogacaoDid(documento.RootElement.Clone(), saga.KeyId, saga.Signature), cancellationToken);
                await store.ConfirmarAsync(saga.CommandId, agora, reconciliada: true, cancellationToken);
            }
            catch (RevogacaoDidRejeitadaException exception)
            {
                await store.RegistrarFalhaDefinitivaAsync(saga.CommandId, exception.Message, agora, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await store.RegistrarFalhaTransitoriaAsync(saga.CommandId, exception.Message, agora, agora.Add(AtrasoTentativa), cancellationToken);
            }
        }
        return pendencias.Count;
    }
}
