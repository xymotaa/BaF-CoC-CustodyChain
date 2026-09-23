namespace CustodyChain.Web.Models.Entities;

public enum SituacaoLacre
{
    INTACTO,
    ROMPIDO,
    SUBSTITUIDO
}

public class Lacre
{
    public long Id { get; set; }
    public long VestigioId { get; set; }
    public required string Numero { get; set; }
    public long? LacreAnteriorId { get; set; }
    public SituacaoLacre Situacao { get; set; }
    public long? AplicadoPorId { get; set; }
    public DateTime? AplicadoEm { get; set; }
    public long? RompidoPorId { get; set; }
    public DateTime? RompidoEm { get; set; }
    public string? JustificativaRompimento { get; set; }
    public long? FotoAnexoId { get; set; }

    public Vestigio Vestigio { get; set; } = null!;
    public Lacre? LacreAnterior { get; set; }
    public Interveniente? AplicadoPor { get; set; }
    public Interveniente? RompidoPor { get; set; }
    public Anexo? FotoAnexo { get; set; }
}
