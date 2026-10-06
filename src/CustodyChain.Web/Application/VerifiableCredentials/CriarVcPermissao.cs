using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.VerifiableCredentials;

public sealed class CriarVcPermissao(IClock clock)
{
    public VcPermissaoSemAssinatura Executar(CriarVcPermissaoInput input)
    {
        var emitidaEm = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        if (string.IsNullOrWhiteSpace(input.DidEmissor) || string.IsNullOrWhiteSpace(input.DidTitular)
            || string.IsNullOrWhiteSpace(input.PerfilTitular)
            || (input.ExpiraEm is not null && input.ExpiraEm <= emitidaEm))
        {
            throw new ArgumentException("Dados inválidos para emissão da VC de permissão.");
        }

        var id = $"urn:uuid:{Guid.NewGuid()}";
        var subject = new Dictionary<string, object?>
        {
            ["id"] = input.DidTitular,
            ["perfil"] = input.PerfilTitular
        };
        if (input.ProcessoId is not null)
        {
            subject["processoId"] = input.ProcessoId.Value.ToString();
        }

        var credential = new Dictionary<string, object?>
        {
            ["@context"] = new[] { "https://www.w3.org/2018/credentials/v1" },
            ["id"] = id,
            ["type"] = new[] { "VerifiableCredential", "CustodyChainPermissionCredential" },
            ["issuer"] = input.DidEmissor,
            ["issuanceDate"] = emitidaEm.ToString("O"),
            ["credentialSubject"] = subject,
            ["credentialStatus"] = new Dictionary<string, string>
            {
                ["id"] = $"{id}#status",
                ["type"] = "CustodyChainLedgerStatusV1"
            }
        };
        if (input.ExpiraEm is not null)
        {
            credential["expirationDate"] = DateTime.SpecifyKind(input.ExpiraEm.Value, DateTimeKind.Utc).ToString("O");
        }

        return new VcPermissaoSemAssinatura(
            id,
            JsonSerializer.SerializeToElement(credential),
            emitidaEm,
            input.ExpiraEm);
    }
}
