namespace CustodyChain.Web.Models.ViewModels;

public record ItemRelatorioVestigioViewModel(
    string RotuloEvidencia,
    string RotuloConjunto,
    string TipoVestigio,
    string Descricao,
    string Estado,
    string? CustodianteNome,
    DateTime? DataHoraColeta,
    DateTime CriadoEm,
    string? HashSha256);

public class RelatorioProcessoViewModel
{
    public long? ProcessoId { get; set; }
    public string? ProcessoNumero { get; set; }
    public string? ProcessoNomeOperacao { get; set; }
    public string? ProcessoOrgaoOrigem { get; set; }
    public DateOnly? ProcessoDataAbertura { get; set; }
    public string? ProcessoSituacao { get; set; }
    public DateTime GeradoEm { get; set; }
    public string? GeradoPorNome { get; set; }

    public IReadOnlyList<ItemRelatorioVestigioViewModel> Vestigios { get; set; } = [];

    public IReadOnlyList<OpcaoProcessoViewModel> ProcessosDisponiveis { get; set; } = [];
}
