namespace CustodyChain.Web.Models.Entities;

public enum SituacaoInscricaoDid
{
    PENDENTE,
    REGISTRADA,
    EXPIRADA
}

public class InscricaoDid
{
    public long Id { get; set; }
    public long IntervenienteId { get; set; }
    public required string EnrollmentId { get; set; }
    public required string CodigoHash { get; set; }
    public SituacaoInscricaoDid Situacao { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime CriadaEm { get; set; }
    public DateTime? RegistradaEm { get; set; }

    public Interveniente Interveniente { get; set; } = null!;
}
