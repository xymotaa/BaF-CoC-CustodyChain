using CustodyChain.Web.Models.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CustodyChain.Web.Services.Relatorios;

public static class GeradorRelatorioProcessoPdf
{
    public static byte[] Gerar(RelatorioProcessoViewModel modelo)
    {
        var documento = Document.Create(container =>
        {
            container.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(2, Unit.Centimetre);
                pagina.DefaultTextStyle(estilo => estilo.FontSize(10).FontFamily("Source Sans 3"));

                pagina.Header().Column(coluna =>
                {
                    coluna.Item().Text("CustodyChain — Relatório do processo").FontSize(16).Bold();
                    coluna.Item().PaddingTop(2).Text(modelo.ProcessoNumero).FontSize(12).FontColor(Colors.Grey.Darken2);
                });

                pagina.Content().PaddingTop(15).Column(coluna =>
                {
                    coluna.Item().Element(c => DesenharDadosProcesso(c, modelo));
                    coluna.Item().PaddingTop(15).Text($"Vestígios ({modelo.Vestigios.Count})").FontSize(12).Bold();
                    coluna.Item().PaddingTop(5).Element(c => DesenharTabelaVestigios(c, modelo.Vestigios));
                });

                pagina.Footer().AlignCenter().Text(texto =>
                {
                    texto.Span($"Gerado em {modelo.GeradoEm:dd/MM/yyyy HH:mm} UTC por {modelo.GeradoPorNome}. Página ");
                    texto.CurrentPageNumber();
                    texto.Span(" de ");
                    texto.TotalPages();
                });
            });
        });

        return documento.GeneratePdf();
    }

    private static void DesenharDadosProcesso(IContainer container, RelatorioProcessoViewModel modelo)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(coluna =>
        {
            coluna.Item().Row(linha =>
            {
                linha.RelativeItem().Text(texto =>
                {
                    texto.Span("Número: ").SemiBold();
                    texto.Span(modelo.ProcessoNumero);
                });
                linha.RelativeItem().Text(texto =>
                {
                    texto.Span("Situação: ").SemiBold();
                    texto.Span(modelo.ProcessoSituacao);
                });
            });
            coluna.Item().PaddingTop(4).Row(linha =>
            {
                linha.RelativeItem().Text(texto =>
                {
                    texto.Span("Operação: ").SemiBold();
                    texto.Span(modelo.ProcessoNomeOperacao ?? "—");
                });
                linha.RelativeItem().Text(texto =>
                {
                    texto.Span("Órgão de origem: ").SemiBold();
                    texto.Span(modelo.ProcessoOrgaoOrigem ?? "—");
                });
            });
            coluna.Item().PaddingTop(4).Text(texto =>
            {
                texto.Span("Data de abertura: ").SemiBold();
                texto.Span(modelo.ProcessoDataAbertura?.ToString("dd/MM/yyyy") ?? "—");
            });
        });
    }

    private static void DesenharTabelaVestigios(IContainer container, IReadOnlyList<ItemRelatorioVestigioViewModel> vestigios)
    {
        container.Table(tabela =>
        {
            tabela.ColumnsDefinition(colunas =>
            {
                colunas.RelativeColumn(2);
                colunas.RelativeColumn(2);
                colunas.RelativeColumn(3);
                colunas.RelativeColumn(2);
                colunas.RelativeColumn(3);
            });

            tabela.Header(cabecalho =>
            {
                foreach (var titulo in new[] { "Rótulo", "Tipo", "Estado", "Custódia atual", "Hash" })
                {
                    cabecalho.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1)
                        .PaddingBottom(4).Text(titulo).SemiBold();
                }
            });

            foreach (var item in vestigios)
            {
                tabela.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(item.RotuloEvidencia);
                tabela.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(item.TipoVestigio);
                tabela.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(item.Estado);
                tabela.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(item.CustodianteNome ?? "—");
                tabela.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4)
                    .Text(item.HashSha256 is { Length: > 0 } h ? h[..Math.Min(16, h.Length)] + "…" : "—")
                    .FontFamily("JetBrains Mono").FontSize(8);
            }
        });
    }
}
