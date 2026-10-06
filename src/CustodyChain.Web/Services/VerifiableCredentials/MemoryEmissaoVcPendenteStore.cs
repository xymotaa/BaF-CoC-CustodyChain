using System.Collections.Concurrent;
using CustodyChain.Web.Application.VerifiableCredentials;

namespace CustodyChain.Web.Services.VerifiableCredentials;

public sealed class MemoryEmissaoVcPendenteStore : IEmissaoVcPendenteStore
{
    private readonly ConcurrentDictionary<string, EmissaoVcPendente> _emissoes = new();

    public void Armazenar(EmissaoVcPendente emissao)
    {
        RemoverExpiradas();
        if (!_emissoes.TryAdd(emissao.EmissaoId, emissao))
        {
            throw new InvalidOperationException("Já existe uma emissão pendente com o mesmo identificador.");
        }
    }

    public bool TentarObter(string emissaoId, out EmissaoVcPendente? emissao)
    {
        RemoverExpiradas();
        return _emissoes.TryGetValue(emissaoId, out emissao);
    }

    public void Remover(string emissaoId) => _emissoes.TryRemove(emissaoId, out _);

    private void RemoverExpiradas()
    {
        foreach (var emissao in _emissoes.Where(item => item.Value.ExpiraEm <= DateTime.UtcNow))
        {
            _emissoes.TryRemove(emissao.Key, out _);
        }
    }
}
