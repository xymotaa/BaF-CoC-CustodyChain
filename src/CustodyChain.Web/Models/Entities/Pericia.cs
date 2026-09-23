namespace CustodyChain.Web.Models.Entities;

public enum PrioridadePericia
{
    NORMAL,
    URGENTE
}

public enum SituacaoPericia
{
    SOLICITADA,
    DESIGNADA,
    RECEBIDA,
    EM_EXECUCAO,
    CONCLUIDA,
    RECUSADA
}

public class Pericia
{
    public long Id { get; set; }
    public long VestigioId { get; set; }
    public long ProcessoId { get; set; }
    public long? PeritoId { get; set; }
    public long? CredencialId { get; set; }
    public string? AreaPericial { get; set; }
    public PrioridadePericia Prioridade { get; set; }
    public DateTime SolicitadaEm { get; set; }
    public DateTime? RecebidaEm { get; set; }
    public DateTime? ConcluidaEm { get; set; }
    public SituacaoPericia Situacao { get; set; }
    public string? MotivoRecusa { get; set; }

    public Vestigio Vestigio { get; set; } = null!;
    public Processo Processo { get; set; } = null!;
    public Interveniente? Perito { get; set; }
    public Credencial? Credencial { get; set; }
    public ICollection<OperacaoAmostra> OperacoesAmostra { get; set; } = [];
    public ICollection<Laudo> Laudos { get; set; } = [];
}
