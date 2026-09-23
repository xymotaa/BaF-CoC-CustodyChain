using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class ConfirmarRecebimentoViewModel
{
    [Required]
    public long MovimentacaoId { get; set; }

    [Required(ErrorMessage = "Informe o número do lacre conferido.")]
    [StringLength(40)]
    [Display(Name = "Número do lacre conferido")]
    public string? NumeroLacreConferido { get; set; }
}

public class RecusarRecebimentoViewModel
{
    [Required]
    public long MovimentacaoId { get; set; }

    [Required(ErrorMessage = "Informe o motivo da recusa.")]
    [Display(Name = "Motivo da recusa")]
    public string? MotivoRecusa { get; set; }
}
