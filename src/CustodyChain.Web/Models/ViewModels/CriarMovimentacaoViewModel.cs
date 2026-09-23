using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class CriarMovimentacaoViewModel
{
    [Required(ErrorMessage = "Selecione o vestígio.")]
    [Display(Name = "Vestígio")]
    public long? VestigioId { get; set; }

    [Required(ErrorMessage = "Selecione o destino.")]
    [Display(Name = "Destino")]
    public long? DestinoId { get; set; }

    [Required(ErrorMessage = "Informe a data e hora de saída.")]
    [Display(Name = "Data e hora de saída")]
    public DateTime? DataHoraSaida { get; set; }

    [StringLength(40)]
    [Display(Name = "Código de rastreamento")]
    public string? CodigoRastreamento { get; set; }

    public IReadOnlyList<OpcaoVestigioViewModel> VestigiosDisponiveis { get; set; } = [];
    public IReadOnlyList<OpcaoIntervenienteViewModel> DestinosDisponiveis { get; set; } = [];
}

public record OpcaoVestigioViewModel(long Id, string RotuloEvidencia, string Descricao);

public record OpcaoIntervenienteViewModel(long Id, string Nome, string Perfil);
