using System.Collections.Concurrent;

namespace CustodyChain.Web.Services.Ledger;

/// <summary>
/// Substitui o gateway/Fabric durante o desenvolvimento das telas.
/// Mantém estado só em memória — nada aqui é persistente entre execuções.
/// </summary>
public class LedgerFake : IServicoLedger
{
    private readonly ConcurrentDictionary<string, DidDocument> _dids = new();
    private readonly ConcurrentDictionary<string, List<EstadoRegistro>> _historico = new();
    private readonly ConcurrentDictionary<string, CredencialCoCRegistrada> _credenciaisCoC = new();
    private int _sequencial;

    public Task RegistrarDidV2PendenteAsync(RegistroDidPendenteDto dto, CancellationToken cancellationToken = default)
    {
        var command = System.Text.Json.JsonSerializer.SerializeToElement(dto.Command);
        var did = command.GetProperty("did").GetString()
            ?? throw new InvalidOperationException("DID inválido.");
        var method = command.GetProperty("metodoDid").GetString()
            ?? throw new InvalidOperationException("Método DID inválido.");
        _dids[did] = new DidDocument(did, method, Ativo: false);
        return Task.CompletedTask;
    }

    public Task AtivarDidV2Async(string did, AtivacaoDidV2Dto dto, CancellationToken cancellationToken = default)
    {
        if (!_dids.TryGetValue(did, out var doc))
            throw new InvalidOperationException($"DID não encontrado: {did}");
        _dids[did] = doc with { Ativo = true };
        return Task.CompletedTask;
    }

    public Task<string> GerarDidAsync(TipoAtor tipo)
    {
        var did = $"did:legal:{tipo.ToString().ToLowerInvariant()}:{Interlocked.Increment(ref _sequencial):D6}";
        _dids[did] = new DidDocument(did, $"did:legal:{tipo.ToString().ToLowerInvariant()}", Ativo: false);
        return Task.FromResult(did);
    }

    public Task AtivarDidAsync(string did, string didEmissor, string senhaEmissor)
    {
        if (!_dids.TryGetValue(did, out var doc))
            throw new InvalidOperationException($"DID não encontrado: {did}");

        _dids[did] = doc with { Ativo = true };
        return Task.CompletedTask;
    }

    public Task<DidDocument> ResolverDidAsync(string did)
    {
        if (!_dids.TryGetValue(did, out var doc))
            throw new InvalidOperationException($"DID não encontrado: {did}");

        return Task.FromResult(doc);
    }

    public Task<string> EmitirCredencialPermissaoAsync(CredencialPermissaoDto dto)
    {
        var credencialId = $"cred-perm-{Guid.NewGuid():N}";
        return Task.FromResult(credencialId);
    }

    public Task<string> EmitirCredencialPermissaoV2Async(
        CredencialPermissaoV2Dto dto,
        CancellationToken cancellationToken = default)
    {
        var credentialId = dto.Credential.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("VC sem identificador.");
        return Task.FromResult(credentialId);
    }

    public Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto, CancellationToken cancellationToken = default)
    {
        var credencialId = dto.CredencialId ?? $"cred-coc-{Guid.NewGuid():N}";

        if (_credenciaisCoC.TryGetValue(credencialId, out var existente))
        {
            var mesmaOperacao = existente.AssetId == dto.AssetId
                && existente.Evento == dto.Evento
                && existente.Did == dto.Did
                && existente.PayloadHashSha256 == dto.PayloadHashSha256;

            if (!mesmaOperacao)
            {
                throw new InvalidOperationException($"Conflito de idempotência para a credencial: {credencialId}");
            }

            return Task.FromResult(credencialId);
        }

        var lista = _historico.GetOrAdd(dto.AssetId, _ => []);
        lock (lista)
        {
            lista.Add(new EstadoRegistro(dto.Evento, DateTime.UtcNow, dto.Did));
        }

        _credenciaisCoC[credencialId] = new CredencialCoCRegistrada(
            credencialId, dto.AssetId, dto.Evento, dto.Did, dto.PayloadHashSha256, Revogada: false);

        return Task.FromResult(credencialId);
    }

    public Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson)
    {
        if (!_credenciaisCoC.TryGetValue(credencialJson, out var credencial))
        {
            return Task.FromResult(new ResultadoVerificacao(Valido: false, Motivo: "Credencial não encontrada no ledger."));
        }

        return Task.FromResult(credencial.Revogada
            ? new ResultadoVerificacao(Valido: false, Motivo: "Credencial revogada.")
            : new ResultadoVerificacao(Valido: true, Motivo: null));
    }

    public Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId)
    {
        var lista = _historico.GetOrAdd(assetId, _ => []);
        IReadOnlyList<EstadoRegistro> copia;
        lock (lista)
        {
            copia = lista.ToList();
        }

        return Task.FromResult(copia);
    }

    public Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credencialId)
    {
        if (!_credenciaisCoC.TryGetValue(credencialId, out var credencial))
            throw new InvalidOperationException($"Credencial não encontrada: {credencialId}");

        return Task.FromResult(credencial);
    }
}
