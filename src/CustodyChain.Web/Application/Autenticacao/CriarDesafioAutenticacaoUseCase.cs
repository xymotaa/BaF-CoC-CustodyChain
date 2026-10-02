using System.Globalization;
using System.Text;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.Autenticacao;

public sealed class CriarDesafioAutenticacaoUseCase(
    IDidRegistry didRegistry,
    IDesafioAutenticacaoStore desafioStore,
    IGeradorNonce geradorNonce,
    IClock clock,
    ConfiguracaoAutenticacaoDid configuracao)
{
    public async Task<DesafioAutenticacao> ExecutarAsync(
        string did,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(did))
        {
            throw CredencialIndisponivel();
        }

        var documento = await didRegistry.ResolverAsync(did.Trim(), cancellationToken);
        if (documento is null || documento.Version != 2 || !documento.Ativo
            || !string.Equals(documento.Status, "ATIVO", StringComparison.Ordinal))
        {
            throw CredencialIndisponivel();
        }

        var metodo = documento.VerificationMethod.FirstOrDefault(m =>
            documento.Authentication.Contains(m.Id, StringComparer.Ordinal));
        if (metodo is null || string.IsNullOrWhiteSpace(metodo.PublicKeyMultibase))
        {
            throw CredencialIndisponivel();
        }

        var emitidoEm = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var expiraEm = emitidoEm.Add(configuracao.ChallengeLifetime);
        var challengeId = Guid.NewGuid().ToString("N");
        var nonce = Base64Url(geradorNonce.Gerar(32));
        var conteudo = CriarConteudoAssinatura(
            challengeId,
            documento.Did,
            nonce,
            configuracao.Audience,
            emitidoEm,
            expiraEm);
        var bytes = Encoding.UTF8.GetBytes(conteudo);

        desafioStore.Armazenar(new DesafioAutenticacaoArmazenado(
            challengeId,
            documento.Did,
            metodo.Id,
            bytes,
            expiraEm));

        return new DesafioAutenticacao(
            challengeId,
            documento.Did,
            metodo.Id,
            Base64Url(bytes),
            expiraEm);
    }

    private static string CriarConteudoAssinatura(
        string challengeId,
        string did,
        string nonce,
        string audience,
        DateTime emitidoEm,
        DateTime expiraEm) =>
        string.Join('\n',
            "custodychain-auth-v1",
            $"challengeId:{challengeId}",
            $"did:{did}",
            $"nonce:{nonce}",
            $"audience:{audience}",
            $"issuedAt:{emitidoEm.ToString("O", CultureInfo.InvariantCulture)}",
            $"expiresAt:{expiraEm.ToString("O", CultureInfo.InvariantCulture)}");

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static AutenticacaoException CredencialIndisponivel() =>
        new(CodigosErroAutenticacao.CredencialIndisponivel,
            "A credencial DID não está ativa ou não possui uma chave de autenticação válida.");
}
