namespace CustodyChain.Web.Application.Recebimento;

public sealed class ValidacaoRecebimentoException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class ConflitoRecebimentoException(string message) : Exception(message);

public sealed class RecursoRecebimentoNaoEncontradoException(string message) : Exception(message);

public sealed class AtorRecebimentoNaoAutorizadoException(string message) : Exception(message);

public sealed class IndisponibilidadeLedgerRecebimentoException(string message, Exception innerException) : Exception(message, innerException);
