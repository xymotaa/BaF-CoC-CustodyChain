namespace CustodyChain.Web.Models.Entities;

public enum SituacaoArmazenamento
{
    GUARDADO,
    RETIRADO,
    VENCIDO
}

public class Armazenamento
{
    public long Id { get; set; }
    public long VestigioId { get; set; }
    public string? Central { get; set; }
    public string? Posicao { get; set; }
    public DateTime EntradaEm { get; set; }
    public DateTime? SaidaEm { get; set; }
    public DateOnly? PrazoGuardaAte { get; set; }
    public long? RecebidoPorId { get; set; }
    public SituacaoArmazenamento Situacao { get; set; }

    public Vestigio Vestigio { get; set; } = null!;
    public Interveniente? RecebidoPor { get; set; }
}
