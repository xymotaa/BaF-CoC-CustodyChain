namespace CustodyChain.Web.Services.Ledger;

/// <summary>
/// Política de roteamento da aplicação. O chaincode continua sendo a fonte de
/// autoridade e repete esta validação antes de aceitar uma escrita.
/// </summary>
public static class OrganizacaoFabricDid
{
    public const string Org1Msp = "Org1MSP";
    public const string Org2Msp = "Org2MSP";

    public static string ResolverMsp(string did)
    {
        if (did.StartsWith("did:legal:admin:", StringComparison.Ordinal)
            || did.StartsWith("did:legal:delegate:", StringComparison.Ordinal))
        {
            return Org1Msp;
        }

        if (did.StartsWith("did:legal:custodian:", StringComparison.Ordinal)
            || did.StartsWith("did:legal:expert:", StringComparison.Ordinal))
        {
            return Org2Msp;
        }

        throw new InvalidOperationException($"DID sem organização Fabric autorizada: {did}");
    }
}
