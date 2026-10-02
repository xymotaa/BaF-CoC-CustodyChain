namespace CustodyChain.Web.Application.Autenticacao;

public static class CodigosErroAutenticacao
{
    public const string CredencialIndisponivel = "credencial_indisponivel";
    public const string DesafioInvalido = "desafio_invalido";
    public const string DesafioExpirado = "desafio_expirado";
    public const string AssinaturaInvalida = "assinatura_invalida";
}

public sealed class AutenticacaoException(string codigo, string message) : Exception(message)
{
    public string Codigo { get; } = codigo;
}
