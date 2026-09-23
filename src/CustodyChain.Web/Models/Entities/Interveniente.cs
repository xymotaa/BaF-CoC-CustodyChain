namespace CustodyChain.Web.Models.Entities;

public enum SituacaoInterveniente
{
    GERADO,
    ATIVO,
    REVOGADO
}

public class Interveniente
{
    public long Id { get; set; }
    public required string Did { get; set; }
    public byte PerfilId { get; set; }
    public required string Nome { get; set; }
    public string? Matricula { get; set; }
    public string? Orgao { get; set; }
    public string? Lotacao { get; set; }
    public SituacaoInterveniente Situacao { get; set; }
    public string? DidEmissor { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtivadoEm { get; set; }

    public Perfil Perfil { get; set; } = null!;
}
