namespace CustodyChain.Web.Application.CadastroVestigio;

public sealed class ValidacaoCadastroVestigioException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class ConflitoCadastroVestigioException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoCadastroVestigioNaoEncontradoException(string message) : Exception(message);

public sealed class AtorCadastroVestigioNaoAutorizadoException(string message) : Exception(message);
public sealed class IndisponibilidadeLedgerCadastroVestigioException(string message, Exception innerException) : Exception(message, innerException);
