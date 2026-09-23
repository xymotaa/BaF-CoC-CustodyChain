namespace CustodyChain.Web.Models.Entities;

public class LogAuditoria
{
    public long Id { get; set; }
    public long? IntervenienteId { get; set; }
    public required string Acao { get; set; }
    public required string Entidade { get; set; }
    public long? RegistroId { get; set; }
    public DateTime DataHora { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public required string HashAnterior { get; set; }
    public required string HashRegistro { get; set; }

    public Interveniente? Interveniente { get; set; }
}
