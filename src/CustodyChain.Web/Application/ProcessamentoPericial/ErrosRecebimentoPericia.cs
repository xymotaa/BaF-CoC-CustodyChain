namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class ValidacaoRecebimentoPericiaException(string message) : Exception(message);

public sealed class RecursoRecebimentoPericiaNaoEncontradoException(string message) : Exception(message);

public sealed class ConflitoRecebimentoPericiaException(string message) : Exception(message);

public sealed class IndisponibilidadeLedgerRecebimentoPericiaException(string message, Exception innerException)
    : Exception(message, innerException);
