using System.ComponentModel.DataAnnotations;

namespace CustodyChain.Web.Models.ViewModels;

public class FracionarViewModel
{
    [Required]
    public long PericiaId { get; set; }

    [Required(ErrorMessage = "Informe o rótulo de evidência do item resultante.")]
    [StringLength(40)]
    [Display(Name = "Rótulo de evidência do fracionamento")]
    public string? RotuloEvidenciaResultante { get; set; }

    [Required(ErrorMessage = "Descreva o item resultante.")]
    [Display(Name = "Descrição do item resultante")]
    public string? DescricaoResultante { get; set; }

    [StringLength(80)]
    [Display(Name = "Quantidade descrita")]
    public string? QuantidadeDescrita { get; set; }

    [Required(ErrorMessage = "Informe a justificativa.")]
    [Display(Name = "Justificativa")]
    public string? Justificativa { get; set; }
}

public class UnificarViewModel
{
    [Required]
    public long PericiaId { get; set; }

    /// <summary>
    /// IDs dos demais vestígios de origem (além do vestígio da própria
    /// perícia), separados por vírgula. Formulário puro sem JS de
    /// seleção múltipla — simples o bastante para o volume esperado.
    /// </summary>
    [Required(ErrorMessage = "Informe ao menos um outro vestígio para unificar (IDs separados por vírgula).")]
    [Display(Name = "IDs dos outros vestígios de origem")]
    public string? OutrosVestigiosOrigemIds { get; set; }

    [Required(ErrorMessage = "Informe o rótulo de evidência do item unificado.")]
    [StringLength(40)]
    [Display(Name = "Rótulo de evidência do item unificado")]
    public string? RotuloEvidenciaResultante { get; set; }

    [Required(ErrorMessage = "Descreva o item unificado.")]
    [Display(Name = "Descrição do item unificado")]
    public string? DescricaoResultante { get; set; }

    [Required(ErrorMessage = "Informe a justificativa.")]
    [Display(Name = "Justificativa")]
    public string? Justificativa { get; set; }
}

public class ConsumirOuExaurirViewModel
{
    [Required]
    public long PericiaId { get; set; }

    [Required]
    [Display(Name = "Tipo")]
    public string Tipo { get; set; } = "CONSUMO";

    [StringLength(80)]
    [Display(Name = "Quantidade descrita")]
    public string? QuantidadeDescrita { get; set; }

    [Required(ErrorMessage = "Informe a justificativa.")]
    [Display(Name = "Justificativa")]
    public string? Justificativa { get; set; }
}
