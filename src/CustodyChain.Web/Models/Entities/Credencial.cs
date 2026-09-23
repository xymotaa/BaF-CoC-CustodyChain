namespace CustodyChain.Web.Models.Entities;

public enum TipoCredencial
{
    PERMISSAO,
    COC
}

public enum SituacaoCredencial
{
    VIGENTE,
    REVOGADA,
    EXPIRADA
}

public class Credencial
{
    public long Id { get; set; }
    public TipoCredencial Tipo { get; set; }
    public required string Identificador { get; set; }
    public long TitularId { get; set; }
    public long EmissorId { get; set; }
    public long? ProcessoId { get; set; }
    public long? VestigioId { get; set; }
    public DateTime EmitidaEm { get; set; }
    public DateTime? ValidaAte { get; set; }
    public SituacaoCredencial Situacao { get; set; }

    public Interveniente Titular { get; set; } = null!;
    public Interveniente Emissor { get; set; } = null!;
    public Processo? Processo { get; set; }
    public Vestigio? Vestigio { get; set; }
}
