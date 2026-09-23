using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public record ItemPericiaViewModel(
    long PericiaId,
    long VestigioId,
    string RotuloEvidencia,
    string Descricao,
    string Situacao,
    string? NumeroLacreAtual,
    bool CredencialPermissaoValida);

public class ProcessamentoPericialListaViewModel
{
    public IReadOnlyList<ItemPericiaViewModel> Pericias { get; set; } = [];
}

public class RomperLacreViewModel
{
    [Required]
    public long PericiaId { get; set; }

    [Required(ErrorMessage = "Informe a justificativa do rompimento.")]
    [Display(Name = "Justificativa do rompimento")]
    public string? Justificativa { get; set; }
}
