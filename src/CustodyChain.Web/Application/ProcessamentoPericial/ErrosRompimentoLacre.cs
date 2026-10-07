namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class ValidacaoRompimentoLacreException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoRompimentoLacreNaoEncontradoException(string message) : Exception(message);

public sealed class CredencialPermissaoInvalidaException(string message) : Exception(message);

public sealed class LacreIntactoNaoEncontradoException(string message) : Exception(message);

public sealed class ConflitoRompimentoLacreException(string message) : Exception(message);

public sealed class AtorRompimentoLacreNaoAutorizadoException(string message) : Exception(message);

public sealed class IndisponibilidadeLedgerRompimentoLacreException(string message, Exception innerException)
    : Exception(message, innerException);
