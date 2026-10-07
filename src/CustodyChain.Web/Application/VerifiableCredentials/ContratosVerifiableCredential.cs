using System.Text.Json;

namespace CustodyChain.Web.Application.VerifiableCredentials;

public sealed record CriarVcPermissaoInput(
    string DidEmissor,
    string DidTitular,
    string PerfilTitular,
    long? ProcessoId,
    DateTime? ExpiraEm,
    string? Identificador = null,
    DateTime? EmitidaEm = null,
    long? VestigioId = null,
    IReadOnlyList<string>? Operacoes = null);

public sealed record VcPermissaoSemAssinatura(
    string Id,
    JsonElement Credencial,
    DateTime EmitidaEm,
    DateTime? ExpiraEm);

public sealed record EmissaoVcPendente(
    string EmissaoId,
    long EmissorId,
    long TitularId,
    long? ProcessoId,
    long? VestigioId,
    VcPermissaoSemAssinatura Credencial,
    DateTime ExpiraEm);

public interface IEmissaoVcPendenteStore
{
    void Armazenar(EmissaoVcPendente emissao);
    bool TentarObter(string emissaoId, out EmissaoVcPendente? emissao);
    void Remover(string emissaoId);
}
