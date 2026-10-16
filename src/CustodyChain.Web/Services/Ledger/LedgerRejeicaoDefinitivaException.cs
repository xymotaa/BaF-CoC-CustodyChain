namespace CustodyChain.Web.Services.Ledger;

/// <summary>Rejeição funcional tipada do gateway: reenviar a mesma prova não altera o resultado.</summary>
public sealed class LedgerRejeicaoDefinitivaException(string codigo, string message) : InvalidOperationException(message)
{
    public string Codigo { get; } = codigo;
}
