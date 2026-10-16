namespace CustodyChain.Web.Application.RevogacaoDid;

public enum MotivoRejeicaoRevogacaoDid
{
    Nenhum,
    IdentidadeIndisponivel,
    AutoRevogacao,
    ResponsabilidadesPendentes,
    ComandoInvalido
}

public static class PoliticaRevogacaoDid
{
    public static MotivoRejeicaoRevogacaoDid Avaliar(
        bool identidadeAtiva,
        string? didAdministrador,
        string? didAlvo,
        bool possuiResponsabilidadesPendentes,
        bool comandoCorresponde) =>
        !identidadeAtiva ? MotivoRejeicaoRevogacaoDid.IdentidadeIndisponivel
        : string.IsNullOrWhiteSpace(didAdministrador) || didAdministrador == didAlvo ? MotivoRejeicaoRevogacaoDid.AutoRevogacao
        : possuiResponsabilidadesPendentes ? MotivoRejeicaoRevogacaoDid.ResponsabilidadesPendentes
        : !comandoCorresponde ? MotivoRejeicaoRevogacaoDid.ComandoInvalido
        : MotivoRejeicaoRevogacaoDid.Nenhum;
}
