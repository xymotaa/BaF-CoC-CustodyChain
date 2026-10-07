namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class ValidacaoEmissaoLaudoException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoEmissaoLaudoNaoEncontradoException(string message) : Exception(message);

public sealed class HashVestigioAusenteException(string message) : Exception(message);

public sealed class ConflitoEmissaoLaudoException(string message) : Exception(message);

public sealed class AtorEmissaoLaudoNaoAutorizadoException(string message) : Exception(message);

public sealed class IndisponibilidadeLedgerEmissaoLaudoException(string message, Exception innerException)
    : Exception(message, innerException);
