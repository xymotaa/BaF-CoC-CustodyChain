using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.Autenticacao;

public sealed class ConcluirAutenticacaoUseCase(
    IDidRegistry didRegistry,
    IDesafioAutenticacaoStore desafioStore,
    IVerificadorAssinaturaDid verificadorAssinatura,
    IIdentidadeAutenticacaoStore identidadeStore,
    IClock clock)
{
    public async Task<IdentidadeAutenticada> ExecutarAsync(
        ProvaAutenticacao prova,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prova.ChallengeId)
            || string.IsNullOrWhiteSpace(prova.Did)
            || string.IsNullOrWhiteSpace(prova.KeyId)
            || string.IsNullOrWhiteSpace(prova.Signature)
            || !desafioStore.TentarConsumir(prova.ChallengeId, out var desafio)
            || desafio is null
            || !string.Equals(desafio.Did, prova.Did, StringComparison.Ordinal)
            || !string.Equals(desafio.KeyId, prova.KeyId, StringComparison.Ordinal))
        {
            throw new AutenticacaoException(
                CodigosErroAutenticacao.DesafioInvalido,
                "O desafio é inválido ou já foi utilizado. Solicite um novo desafio.");
        }

        if (clock.UtcNow >= desafio.ExpiresAt)
        {
            throw new AutenticacaoException(
                CodigosErroAutenticacao.DesafioExpirado,
                "O desafio expirou. Solicite um novo desafio.");
        }

        var documento = await didRegistry.ResolverAsync(prova.Did, cancellationToken);
        if (documento is null || documento.Version != 2 || !documento.Ativo
            || !string.Equals(documento.Status, "ATIVO", StringComparison.Ordinal)
            || !documento.Authentication.Contains(prova.KeyId, StringComparer.Ordinal))
        {
            throw new AutenticacaoException(
                CodigosErroAutenticacao.CredencialIndisponivel,
                "A credencial DID não está ativa ou não possui uma chave de autenticação válida.");
        }

        var metodo = documento.VerificationMethod.FirstOrDefault(m =>
            string.Equals(m.Id, prova.KeyId, StringComparison.Ordinal));
        if (metodo is null || !verificadorAssinatura.Verificar(metodo, desafio.SigningInput, prova.Signature))
        {
            throw new AutenticacaoException(
                CodigosErroAutenticacao.AssinaturaInvalida,
                "A wallet não produziu uma assinatura válida para este desafio.");
        }

        return await identidadeStore.ObterAtivaAsync(prova.Did, cancellationToken)
            ?? throw new AutenticacaoException(
                CodigosErroAutenticacao.CredencialIndisponivel,
                "A credencial DID não corresponde a um interveniente ativo.");
    }
}
