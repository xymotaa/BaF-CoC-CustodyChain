namespace CustodyChain.Web.Application.Pericias;

public sealed class ValidacaoDesignacaoPericiaException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoDesignacaoPericiaNaoEncontradoException(string message) : Exception(message);

public sealed class ConflitoDesignacaoPericiaException(string message) : Exception(message);

public sealed class IndisponibilidadeLedgerDesignacaoPericiaException(string message, Exception innerException)
    : Exception(message, innerException);
