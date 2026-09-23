using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class DesignarPericiaViewModel
{
    [Required(ErrorMessage = "Selecione o vestígio.")]
    [Display(Name = "Vestígio")]
    public long? VestigioId { get; set; }

    [Required(ErrorMessage = "Selecione o perito.")]
    [Display(Name = "Perito")]
    public long? PeritoId { get; set; }

    [StringLength(60)]
    [Display(Name = "Área pericial")]
    public string? AreaPericial { get; set; }

    [Required]
    [Display(Name = "Prioridade")]
    public string Prioridade { get; set; } = "NORMAL";

    public IReadOnlyList<OpcaoVestigioViewModel> VestigiosDisponiveis { get; set; } = [];
    public IReadOnlyList<OpcaoIntervenienteViewModel> PeritosDisponiveis { get; set; } = [];
}
