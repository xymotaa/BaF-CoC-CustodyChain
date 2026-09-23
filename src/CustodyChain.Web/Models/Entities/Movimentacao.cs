namespace CustodyChain.Web.Models.Entities;

public enum TipoMovimentacao
{
    REMESSA,
    TRANSPORTE,
    RECEBIMENTO,
    RECUSA,
    DEVOLUCAO
}

public enum SituacaoMovimentacao
{
    PENDENTE,
    ACEITA,
    RECUSADA
}

public class Movimentacao
{
    public long Id { get; set; }
    public long VestigioId { get; set; }
    public TipoMovimentacao Tipo { get; set; }
    public byte Etapa { get; set; }
    public long? OrigemId { get; set; }
    public long? DestinoId { get; set; }
    public DateTime? DataHoraSaida { get; set; }
    public DateTime? DataHoraChegada { get; set; }
    public bool? CondicoesAdequadas { get; set; }
    public bool? HouveIntercorrencia { get; set; }
    public string? DescricaoIntercorrencia { get; set; }
    public string? CodigoRastreamento { get; set; }
    public SituacaoMovimentacao Situacao { get; set; }
    public string? MotivoRecusa { get; set; }
    public long CriadoPorId { get; set; }
    public long? AprovadoPorId { get; set; }

    public Vestigio Vestigio { get; set; } = null!;
    public Interveniente? Origem { get; set; }
    public Interveniente? Destino { get; set; }
    public Interveniente CriadoPor { get; set; } = null!;
    public Interveniente? AprovadoPor { get; set; }
}
