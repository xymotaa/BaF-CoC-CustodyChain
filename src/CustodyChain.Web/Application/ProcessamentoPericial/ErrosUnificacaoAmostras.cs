namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class ValidacaoUnificacaoAmostrasException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}
public sealed class RecursoUnificacaoAmostrasNaoEncontradoException(string message) : Exception(message);
public sealed class ConflitoUnificacaoAmostrasException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}
public sealed class AtorUnificacaoAmostrasNaoAutorizadoException(string message) : Exception(message);
public sealed class IndisponibilidadeLedgerUnificacaoAmostrasException(string message, Exception innerException)
    : Exception(message, innerException);
