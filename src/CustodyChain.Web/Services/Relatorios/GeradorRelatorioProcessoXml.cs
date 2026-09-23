using System.Xml.Linq;
using CustodyChain.Web.Models.ViewModels;

namespace CustodyChain.Web.Services.Relatorios;

public static class GeradorRelatorioProcessoXml
{
    public static string Gerar(RelatorioProcessoViewModel modelo)
    {
        var documento = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("RelatorioProcesso",
                new XAttribute("geradoEm", modelo.GeradoEm.ToString("o")),
                new XAttribute("geradoPor", modelo.GeradoPorNome ?? ""),
                new XElement("Processo",
                    new XElement("Numero", modelo.ProcessoNumero),
                    new XElement("NomeOperacao", modelo.ProcessoNomeOperacao ?? ""),
                    new XElement("OrgaoOrigem", modelo.ProcessoOrgaoOrigem ?? ""),
                    new XElement("DataAbertura", modelo.ProcessoDataAbertura?.ToString("yyyy-MM-dd") ?? ""),
                    new XElement("Situacao", modelo.ProcessoSituacao)),
                new XElement("Vestigios",
                    modelo.Vestigios.Select(v => new XElement("Vestigio",
                        new XElement("RotuloEvidencia", v.RotuloEvidencia),
                        new XElement("RotuloConjunto", v.RotuloConjunto),
                        new XElement("Tipo", v.TipoVestigio),
                        new XElement("Descricao", v.Descricao),
                        new XElement("Estado", v.Estado),
                        new XElement("CustodianteAtual", v.CustodianteNome ?? ""),
                        new XElement("DataHoraColeta", v.DataHoraColeta?.ToString("o") ?? ""),
                        new XElement("CriadoEm", v.CriadoEm.ToString("o")),
                        new XElement("HashSha256", v.HashSha256 ?? ""))))));

        return documento.Declaration + Environment.NewLine + documento.ToString();
    }
}
