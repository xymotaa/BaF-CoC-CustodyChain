namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class ValidacaoFracionamentoAmostraException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoFracionamentoAmostraNaoEncontradoException(string message) : Exception(message);

public sealed class ConflitoFracionamentoAmostraException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class AtorFracionamentoAmostraNaoAutorizadoException(string message) : Exception(message);

public sealed class IndisponibilidadeLedgerFracionamentoAmostraException(string message, Exception innerException) : Exception(message, innerException);
