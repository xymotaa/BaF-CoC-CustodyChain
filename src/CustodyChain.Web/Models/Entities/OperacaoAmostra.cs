namespace CustodyChain.Web.Models.Entities;

public enum TipoOperacaoAmostra
{
    FRACIONAMENTO,
    UNIFICACAO,
    CONSUMO,
    EXAURIMENTO
}

public class OperacaoAmostra
{
    public long Id { get; set; }
    public long PericiaId { get; set; }
    public TipoOperacaoAmostra Tipo { get; set; }
    public long VestigioOrigemId { get; set; }
    public long? VestigioResultanteId { get; set; }
    public string? QuantidadeDescrita { get; set; }
    public required string Justificativa { get; set; }
    public long? ExecutadoPorId { get; set; }
    public DateTime ExecutadoEm { get; set; }

    public Pericia Pericia { get; set; } = null!;
    public Vestigio VestigioOrigem { get; set; } = null!;
    public Vestigio? VestigioResultante { get; set; }
    public Interveniente? ExecutadoPor { get; set; }
}
