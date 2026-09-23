using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Selecione uma credencial DID.")]
    [Display(Name = "Credencial DID")]
    public string? Did { get; set; }

    [Required(ErrorMessage = "Informe a senha da wallet.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha da wallet")]
    public string? Senha { get; set; }

    public IReadOnlyList<OpcaoDidViewModel> CredenciaisDisponiveis { get; set; } = [];
}

public record OpcaoDidViewModel(string Did, string Nome, string Perfil);
