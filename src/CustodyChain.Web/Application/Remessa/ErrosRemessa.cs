namespace CustodyChain.Web.Application.Remessa;

public sealed class ValidacaoRemessaException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class ConflitoRemessaException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoRemessaNaoEncontradoException(string message) : Exception(message);

public sealed class AtorRemessaNaoAutorizadoException(string message) : Exception(message);
