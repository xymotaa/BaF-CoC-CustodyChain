namespace CustodyChain.Web.Models.Entities;

public enum TipoDescarte
{
    DESCARTE,
    RESTITUICAO
}

public class Descarte
{
    public long Id { get; set; }
    public long VestigioId { get; set; }
    public TipoDescarte Tipo { get; set; }
    public long AutorizacaoAnexoId { get; set; }
    public required string DidMagistrado { get; set; }
    public long? SolicitadoPorId { get; set; }
    public long? AprovadoPorId { get; set; }
    public DateTime? ExecutadoEm { get; set; }
    public string? Observacao { get; set; }

    public Vestigio Vestigio { get; set; } = null!;
    public Anexo AutorizacaoAnexo { get; set; } = null!;
    public Interveniente? SolicitadoPor { get; set; }
    public Interveniente? AprovadoPor { get; set; }
}
