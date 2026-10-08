using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;

namespace CustodyChain.Web.Services.Autenticacao;

public sealed class DidRegistryFabric(HttpClient httpClient) : IDidRegistry
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    public async Task<DocumentoDidAutenticacao?> ResolverAsync(
        string did,
        CancellationToken cancellationToken = default)
    {
        using var resposta = await httpClient.GetAsync(
            $"/v2/dids/{Uri.EscapeDataString(did)}",
            cancellationToken);
        if (resposta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!resposta.IsSuccessStatusCode)
        {
            var corpo = await resposta.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Falha ao resolver DID no gateway ({(int)resposta.StatusCode}): {corpo}");
        }

        var dto = await resposta.Content.ReadFromJsonAsync<DocumentoDidDto>(OpcoesJson, cancellationToken)
            ?? throw new InvalidOperationException("Resposta vazia do gateway ao resolver DID.");

        return new DocumentoDidAutenticacao(
            dto.Id ?? dto.Did ?? string.Empty,
            dto.Version,
            dto.Status,
            dto.Ativo,
            dto.VerificationMethod
                .Select(m => new MetodoVerificacaoDid(m.Id, m.Type, m.Controller, m.PublicKeyMultibase))
                .ToArray(),
            dto.Authentication,
            dto.DocumentVersion,
            dto.CapabilityInvocation);
    }

    public static void ConfigurarAutorizacao(HttpClient client, string? serviceToken)
    {
        if (!string.IsNullOrWhiteSpace(serviceToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        }
    }

    private sealed record DocumentoDidDto(
        string? Id,
        string? Did,
        int Version,
        string Status,
        bool Ativo,
        int DocumentVersion,
        IReadOnlyList<MetodoVerificacaoDto> VerificationMethod,
        IReadOnlyList<string> Authentication,
        IReadOnlyList<string>? CapabilityInvocation);

    private sealed record MetodoVerificacaoDto(
        string Id,
        string Type,
        string Controller,
        string PublicKeyMultibase);
}
