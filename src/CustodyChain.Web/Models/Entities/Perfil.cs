namespace CustodyChain.Web.Models.Entities;

public enum CodigoPerfil
{
    ADMIN,
    CUSTODIA,
    COLETOR,
    PERITO,
    EXTERNO
}

public class Perfil
{
    public byte Id { get; set; }
    public required string Codigo { get; set; }
    public required string Nome { get; set; }
    public required string MetodoDid { get; set; }

    public ICollection<Interveniente> Intervenientes { get; set; } = [];
}
