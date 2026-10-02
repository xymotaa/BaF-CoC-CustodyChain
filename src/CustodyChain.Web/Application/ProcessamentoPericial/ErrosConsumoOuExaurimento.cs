namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class ValidacaoConsumoOuExaurimentoException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoConsumoOuExaurimentoNaoEncontradoException(string message) : Exception(message);

public sealed class ConflitoConsumoOuExaurimentoException(string message) : Exception(message);

public sealed class AtorConsumoOuExaurimentoNaoAutorizadoException(string message) : Exception(message);
