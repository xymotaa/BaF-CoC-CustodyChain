namespace CustodyChain.Web.Models.Entities;

public class Laudo
{
    public long Id { get; set; }
    public long PericiaId { get; set; }
    public required string Numero { get; set; }
    public short Versao { get; set; }
    public long? LaudoAnteriorId { get; set; }
    public string? Conteudo { get; set; }
    public required string HashVestigios { get; set; }
    public required string HashLaudo { get; set; }
    public string? AssinaturaEd25519 { get; set; }
    public long? AssinadoPorId { get; set; }
    public DateTime? AssinadoEm { get; set; }
    public long? AnexoPdfId { get; set; }

    public Pericia Pericia { get; set; } = null!;
    public Laudo? LaudoAnterior { get; set; }
    public Interveniente? AssinadoPor { get; set; }
    public Anexo? AnexoPdf { get; set; }
}
