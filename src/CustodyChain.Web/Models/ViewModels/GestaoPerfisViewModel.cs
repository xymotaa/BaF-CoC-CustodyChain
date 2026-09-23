using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class CadastrarIntervenienteViewModel
{
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(120)]
    [Display(Name = "Nome")]
    public string? Nome { get; set; }

    [StringLength(30)]
    [Display(Name = "Matrícula")]
    public string? Matricula { get; set; }

    [StringLength(80)]
    [Display(Name = "Órgão")]
    public string? Orgao { get; set; }

    [StringLength(80)]
    [Display(Name = "Lotação")]
    public string? Lotacao { get; set; }

    [Required(ErrorMessage = "Selecione o perfil.")]
    [Display(Name = "Perfil")]
    public byte? PerfilId { get; set; }

    public IReadOnlyList<OpcaoPerfilViewModel> PerfisDisponiveis { get; set; } = [];
}

public record OpcaoPerfilViewModel(byte Id, string Codigo, string Nome);

public record ItemIntervenienteViewModel(
    long Id,
    string Did,
    string Nome,
    string PerfilNome,
    string Situacao,
    DateTime CriadoEm,
    DateTime? AtivadoEm);

public class GestaoPerfisListaViewModel
{
    public IReadOnlyList<ItemIntervenienteViewModel> Intervenientes { get; set; } = [];
}

public class EmitirCredencialPermissaoViewModel
{
    [Required(ErrorMessage = "Selecione o titular.")]
    [Display(Name = "Titular")]
    public long? TitularId { get; set; }

    [Display(Name = "Processo (opcional)")]
    public long? ProcessoId { get; set; }

    [Display(Name = "Válida até (opcional)")]
    public DateTime? ValidaAte { get; set; }

    public IReadOnlyList<ItemIntervenienteViewModel> TitularesDisponiveis { get; set; } = [];
    public IReadOnlyList<OpcaoProcessoViewModel> ProcessosDisponiveis { get; set; } = [];
}
