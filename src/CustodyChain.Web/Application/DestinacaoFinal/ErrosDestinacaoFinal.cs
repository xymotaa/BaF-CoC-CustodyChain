namespace CustodyChain.Web.Application.DestinacaoFinal;

public sealed class ValidacaoDestinacaoFinalException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoDestinacaoFinalNaoEncontradoException(string message) : Exception(message);

public sealed class ConflitoDestinacaoFinalException(string message) : Exception(message);

public sealed class AtorDestinacaoFinalNaoAutorizadoException(string message) : Exception(message);

public sealed class IndisponibilidadeLedgerDestinacaoFinalException(string message, Exception innerException) : Exception(message, innerException);
