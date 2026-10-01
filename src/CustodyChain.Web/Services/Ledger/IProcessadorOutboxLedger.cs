namespace CustodyChain.Web.Services.Ledger;

public interface IProcessadorOutboxLedger
{
    Task<int> ProcessarLoteAsync(CancellationToken cancellationToken = default);
}
