using CustodyChain.Web.Models.Entities;

namespace CustodyChain.Web.Data;

public static class IntervenienteQueryExtensions
{
    public static IQueryable<Interveniente> AptosParaOperacoesLedger(
        this IQueryable<Interveniente> intervenientes) =>
        intervenientes.Where(interveniente =>
            interveniente.Situacao == SituacaoInterveniente.ATIVO
            && interveniente.SituacaoIdentidadeLedger == SituacaoIdentidadeLedger.ATIVA);
}
