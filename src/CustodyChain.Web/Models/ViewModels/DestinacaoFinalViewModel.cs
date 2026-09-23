using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace CustodyChain.Web.Models.ViewModels;

public class SolicitarDestinacaoViewModel
{
    [Required(ErrorMessage = "Selecione o vestígio.")]
    [Display(Name = "Vestígio")]
    public long? VestigioId { get; set; }

    [Required]
    [Display(Name = "Tipo")]
    public string Tipo { get; set; } = "DESCARTE";

    [Required(ErrorMessage = "Informe o DID do magistrado que autorizou.")]
    [StringLength(200)]
    [Display(Name = "DID do magistrado")]
    public string? DidMagistrado { get; set; }

    [Required(ErrorMessage = "Anexe o mandado judicial.")]
    [Display(Name = "Mandado judicial (autorização)")]
    public IFormFile? ArquivoAutorizacao { get; set; }

    [Display(Name = "Observação")]
    public string? Observacao { get; set; }

    public IReadOnlyList<OpcaoVestigioViewModel> VestigiosDisponiveis { get; set; } = [];
}

public record ItemDestinacaoViewModel(
    long DescarteId,
    long VestigioId,
    string RotuloEvidencia,
    string Tipo,
    string DidMagistrado,
    string? Observacao,
    string SolicitanteNome,
    long AnexoId);
