namespace CustodyChain.Web.Services.Ledger;

/// <summary>Resposta 4xx do gateway: reenviar a mesma prova não altera o resultado.</summary>
public sealed class LedgerRejeicaoDefinitivaException(string message) : InvalidOperationException(message);
