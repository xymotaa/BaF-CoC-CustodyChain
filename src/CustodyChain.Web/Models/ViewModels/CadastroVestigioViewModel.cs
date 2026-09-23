using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class CadastroVestigioViewModel
{
    // Lacre Digital (Quadro 34) — RE e RC

    [Required(ErrorMessage = "Informe o rótulo de evidência.")]
    [StringLength(40)]
    [Display(Name = "Rótulo de evidência (RE)")]
    public string? RotuloEvidencia { get; set; }

    [Required(ErrorMessage = "Informe o rótulo de conjunto.")]
    [StringLength(40)]
    [Display(Name = "Rótulo de conjunto (RC)")]
    public string? RotuloConjunto { get; set; }

    [StringLength(40)]
    [Display(Name = "Número da evidência (NE)")]
    public string? NumeroEvidencia { get; set; }

    [Required(ErrorMessage = "Selecione o processo (Número do Caso).")]
    [Display(Name = "Processo (NC)")]
    public long? ProcessoId { get; set; }

    [Required(ErrorMessage = "Informe a descrição da evidência.")]
    [Display(Name = "Descrição da evidência (DE)")]
    public string? Descricao { get; set; }

    // Identificação do vestígio (10.3)

    [Required(ErrorMessage = "Selecione o tipo de vestígio.")]
    [Display(Name = "Tipo de vestígio")]
    public short? TipoVestigioId { get; set; }

    [StringLength(200)]
    [Display(Name = "Localização / local de exame")]
    public string? LocalColeta { get; set; }

    // Coleta (10.4)

    [Required(ErrorMessage = "Informe a data e hora da coleta.")]
    [Display(Name = "Data e hora da coleta (DH)")]
    public DateTime? DataHoraColeta { get; set; }

    [Display(Name = "Métodos e procedimentos utilizados")]
    public string? MetodoColeta { get; set; }

    [Required(ErrorMessage = "Informe o número do lacre físico.")]
    [StringLength(40)]
    [Display(Name = "Número do lacre físico")]
    public string? NumeroLacre { get; set; }

    [Display(Name = "Houve intercorrência?")]
    public bool HouveIntercorrencia { get; set; }

    [Display(Name = "Descrição da intercorrência")]
    public string? DescricaoIntercorrencia { get; set; }

    // Opções para os seletores

    public IReadOnlyList<OpcaoProcessoViewModel> ProcessosDisponiveis { get; set; } = [];
    public IReadOnlyList<OpcaoTipoVestigioViewModel> TiposDisponiveis { get; set; } = [];
}

public record OpcaoProcessoViewModel(long Id, string Numero, string? NomeOperacao);

public record OpcaoTipoVestigioViewModel(short Id, string Descricao, string Categoria);
