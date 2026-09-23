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
    private int _sequencial;

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

    public Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto)
    {
        var credencialId = $"cred-coc-{Guid.NewGuid():N}";

        var lista = _historico.GetOrAdd(dto.AssetId, _ => []);
        lock (lista)
        {
            lista.Add(new EstadoRegistro(dto.Evento, DateTime.UtcNow, dto.Did));
        }

        return Task.FromResult(credencialId);
    }

    public Task<ResultadoVerificacao> VerificarCredencialAsync(string credencialJson)
    {
        return Task.FromResult(new ResultadoVerificacao(Valido: true, Motivo: null));
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
}
