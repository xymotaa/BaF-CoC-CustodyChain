namespace CustodyChain.Web.Models.ViewModels;

public class LoginViewModel
{
    public required string WalletEndpoint { get; init; }
}

public sealed record CriarDesafioLoginRequest(string? Did);

public sealed record ConcluirLoginRequest(
    string? ChallengeId,
    string? Did,
    string? KeyId,
    string? Signature);
