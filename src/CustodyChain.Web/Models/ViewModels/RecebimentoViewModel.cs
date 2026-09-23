namespace CustodyChain.Web.Models.ViewModels;

public record ItemRecebimentoViewModel(
    long MovimentacaoId,
    long VestigioId,
    string RotuloEvidencia,
    string Descricao,
    string OrigemNome,
    DateTime? DataHoraSaida,
    string? CodigoRastreamento,
    string NumeroLacreEsperado);

public class RecebimentoListaViewModel
{
    public IReadOnlyList<ItemRecebimentoViewModel> Pendentes { get; set; } = [];
}
