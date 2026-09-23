using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class DarEntradaArquivoViewModel
{
    [Required(ErrorMessage = "Selecione o vestígio.")]
    [Display(Name = "Vestígio")]
    public long? VestigioId { get; set; }

    [Required(ErrorMessage = "Informe a central de custódia.")]
    [StringLength(80)]
    [Display(Name = "Central")]
    public string? Central { get; set; }

    [StringLength(40)]
    [Display(Name = "Posição na guarda")]
    public string? Posicao { get; set; }

    [Display(Name = "Prazo de guarda até")]
    public DateOnly? PrazoGuardaAte { get; set; }

    public IReadOnlyList<OpcaoVestigioViewModel> VestigiosDisponiveis { get; set; } = [];
}

public record ItemArquivoViewModel(
    long VestigioId,
    string RotuloEvidencia,
    string RotuloConjunto,
    string Descricao,
    string TipoVestigio,
    string CategoriaVestigio,
    string ProcessoNumero,
    string? CustodianteNome,
    string Estado,
    string HashSha256,
    string? Central,
    string? Posicao,
    DateOnly? PrazoGuardaAte,
    bool PrazoVencido);

public class ArquivoListaViewModel
{
    public string? Busca { get; set; }
    public string? CategoriaFiltro { get; set; }
    public IReadOnlyList<ItemArquivoViewModel> Itens { get; set; } = [];
    public IReadOnlyList<string> Categorias { get; set; } = [];
}
