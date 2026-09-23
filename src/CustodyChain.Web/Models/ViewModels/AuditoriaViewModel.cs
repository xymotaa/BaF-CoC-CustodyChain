using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace CustodyChain.Web.Models.ViewModels;

public record EventoLinhaDoTempoViewModel(
    long RegistroId,
    string EntidadeOrigem,
    string Evento,
    string Estado,
    DateTime CriadoEm,
    DateTime? AncoradoEm,
    string? TxHash);

public class LinhaDoTempoViewModel
{
    public string? Busca { get; set; }
    public long? VestigioId { get; set; }
    public string? RotuloEvidencia { get; set; }
    public string? Descricao { get; set; }
    public string? EstadoAtual { get; set; }
    public string? HashSha256 { get; set; }
    public IReadOnlyList<EventoLinhaDoTempoViewModel> Eventos { get; set; } = [];
}

public class VerificadorViewModel
{
    [Required(ErrorMessage = "Informe o rótulo de evidência.")]
    [Display(Name = "Rótulo de evidência (RE)")]
    public string? RotuloEvidencia { get; set; }

    [Required(ErrorMessage = "Selecione o arquivo a conferir.")]
    [Display(Name = "Arquivo")]
    public IFormFile? Arquivo { get; set; }

    public bool? Conferido { get; set; }
    public string? HashCalculado { get; set; }
    public string? HashRegistrado { get; set; }
    public string? MensagemResultado { get; set; }
}
