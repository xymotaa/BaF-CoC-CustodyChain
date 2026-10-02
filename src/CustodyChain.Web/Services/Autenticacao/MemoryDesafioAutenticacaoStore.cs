using System.Collections.Concurrent;
using CustodyChain.Web.Application.Autenticacao;

namespace CustodyChain.Web.Services.Autenticacao;

public sealed class MemoryDesafioAutenticacaoStore : IDesafioAutenticacaoStore
{
    private readonly ConcurrentDictionary<string, DesafioAutenticacaoArmazenado> _desafios = new();

    public void Armazenar(DesafioAutenticacaoArmazenado desafio)
    {
        foreach (var expirado in _desafios.Where(item => item.Value.ExpiresAt <= DateTime.UtcNow))
        {
            _desafios.TryRemove(expirado.Key, out _);
        }

        if (!_desafios.TryAdd(desafio.ChallengeId, desafio))
        {
            throw new InvalidOperationException("Já existe um desafio com o mesmo identificador.");
        }
    }

    public bool TentarConsumir(string challengeId, out DesafioAutenticacaoArmazenado? desafio) =>
        _desafios.TryRemove(challengeId, out desafio);
}
