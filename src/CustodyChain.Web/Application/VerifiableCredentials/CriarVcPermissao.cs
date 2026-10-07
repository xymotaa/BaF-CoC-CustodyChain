using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.VerifiableCredentials;

public sealed class CriarVcPermissao(IClock clock)
{
    public VcPermissaoSemAssinatura Executar(CriarVcPermissaoInput input)
    {
        var emitidaEm = DateTime.SpecifyKind(input.EmitidaEm ?? clock.UtcNow, DateTimeKind.Utc);
        var possuiAutorizacao = input.Operacoes is not null;
        var operacoesInvalidas = input.Operacoes is { Count: 0 }
            || input.Operacoes?.Any(string.IsNullOrWhiteSpace) == true;
        if (string.IsNullOrWhiteSpace(input.DidEmissor) || string.IsNullOrWhiteSpace(input.DidTitular)
            || string.IsNullOrWhiteSpace(input.PerfilTitular)
            || (input.ExpiraEm is not null && input.ExpiraEm <= emitidaEm)
            || (input.Identificador is not null && string.IsNullOrWhiteSpace(input.Identificador))
            || (possuiAutorizacao && (input.ProcessoId is null or <= 0 || operacoesInvalidas))
            || (!possuiAutorizacao && input.VestigioId is not null)
            || (input.VestigioId is not null && input.VestigioId <= 0))
        {
            throw new ArgumentException("Dados inválidos para emissão da VC de permissão.");
        }

        var id = input.Identificador ?? $"urn:uuid:{Guid.NewGuid()}";
        var subject = new Dictionary<string, object?>
        {
            ["id"] = input.DidTitular,
            ["perfil"] = input.PerfilTitular
        };
        if (possuiAutorizacao)
        {
            var authorization = new Dictionary<string, object?>
            {
                ["processoId"] = input.ProcessoId!.Value.ToString(),
                ["operations"] = input.Operacoes!.ToArray()
            };
            if (input.VestigioId is not null)
                authorization["assetId"] = input.VestigioId.Value.ToString();
            subject["authorization"] = authorization;
        }
        else if (input.ProcessoId is not null)
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
