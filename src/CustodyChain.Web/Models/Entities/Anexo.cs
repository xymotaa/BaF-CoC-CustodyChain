namespace CustodyChain.Web.Models.Entities;

public enum TipoAnexo
{
    FOTOGRAFIA,
    DOCUMENTO,
    IMAGEM_FORENSE,
    LAUDO,
    AUTORIZACAO,
    OUTRO
}

public class Anexo
{
    public long Id { get; set; }
    public long VestigioId { get; set; }
    public TipoAnexo Tipo { get; set; }
    public required string NomeArquivo { get; set; }
    public string? CaminhoRelativo { get; set; }
    public long? TamanhoBytes { get; set; }
    public required string HashSha256 { get; set; }
    public string? Algoritmo { get; set; }
    public long? EnviadoPorId { get; set; }
    public DateTime EnviadoEm { get; set; }

    public Vestigio Vestigio { get; set; } = null!;
    public Interveniente? EnviadoPor { get; set; }
}
