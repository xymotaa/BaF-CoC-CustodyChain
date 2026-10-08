namespace CustodyChain.Web.Application.Autenticacao;

public sealed record MetodoVerificacaoDid(
    string Id,
    string Tipo,
    string Controller,
    string PublicKeyMultibase);

public sealed record DocumentoDidAutenticacao(
    string Did,
    int Version,
    string Status,
    bool Ativo,
    IReadOnlyList<MetodoVerificacaoDid> VerificationMethod,
    IReadOnlyList<string> Authentication,
    int DocumentVersion = 1,
    IReadOnlyList<string>? CapabilityInvocation = null);

public sealed record DesafioAutenticacao(
    string ChallengeId,
    string Did,
    string KeyId,
    string SigningInput,
    DateTime ExpiresAt);

public sealed record DesafioAutenticacaoArmazenado(
    string ChallengeId,
    string Did,
    string KeyId,
    byte[] SigningInput,
    DateTime ExpiresAt);

public sealed record ProvaAutenticacao(
    string ChallengeId,
    string Did,
    string KeyId,
    string Signature);

public sealed record IdentidadeAutenticada(
    long Id,
    string Nome,
    string Did,
    string PerfilCodigo,
    string PerfilNome,
    string? KeyId = null,
    int DocumentVersion = 1);

public sealed record ConfiguracaoAutenticacaoDid(
    string Audience,
    TimeSpan ChallengeLifetime);

public interface IDidRegistry
{
    Task<DocumentoDidAutenticacao?> ResolverAsync(string did, CancellationToken cancellationToken = default);
}

public interface IDesafioAutenticacaoStore
{
    void Armazenar(DesafioAutenticacaoArmazenado desafio);
    bool TentarConsumir(string challengeId, out DesafioAutenticacaoArmazenado? desafio);
}

public interface IVerificadorAssinaturaDid
{
    bool Verificar(MetodoVerificacaoDid metodo, ReadOnlySpan<byte> mensagem, string assinaturaBase64Url);
}

public interface IIdentidadeAutenticacaoStore
{
    Task<IdentidadeAutenticada?> ObterAtivaAsync(string did, CancellationToken cancellationToken = default);
}

public interface IGeradorNonce
{
    byte[] Gerar(int quantidadeBytes);
}
