namespace CustodyChain.Web.Application.Arquivo;

public sealed class ValidacaoEntradaArquivoException(string message, string? campo = null) : Exception(message)
{
    public string? Campo { get; } = campo;
}

public sealed class RecursoEntradaArquivoNaoEncontradoException(string message) : Exception(message);

public sealed class ConflitoEntradaArquivoException(string message) : Exception(message);

public sealed class AtorEntradaArquivoNaoAutorizadoException(string message) : Exception(message);
