using CustodyChain.Web.Application.Autenticacao;

namespace CustodyChain.Web.Application.RevogacaoDid;

internal enum ResolucaoConflitoVersaoRevogacaoDid
{
    ConfirmarRevogacao,
    RejeitarComando,
    ManterPendente
}

internal static class PoliticaConflitoVersaoRevogacaoDid
{
    public static ResolucaoConflitoVersaoRevogacaoDid Avaliar(DocumentoDidAutenticacao? documento) =>
        documento is { Status: "REVOGADO", Ativo: false }
            ? ResolucaoConflitoVersaoRevogacaoDid.ConfirmarRevogacao
            : documento is { Version: 2, Status: "ATIVO", Ativo: true }
                ? ResolucaoConflitoVersaoRevogacaoDid.RejeitarComando
                : ResolucaoConflitoVersaoRevogacaoDid.ManterPendente;
}
