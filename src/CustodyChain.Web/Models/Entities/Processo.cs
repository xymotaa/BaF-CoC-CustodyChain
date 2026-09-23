namespace CustodyChain.Web.Models.Entities;

public enum SituacaoProcesso
{
    ATIVO,
    SUSPENSO,
    ENCERRADO
}

public class Processo
{
    public long Id { get; set; }
    public required string Numero { get; set; }
    public string? NomeOperacao { get; set; }
    public string? OrgaoOrigem { get; set; }
    public DateOnly? DataAbertura { get; set; }
    public SituacaoProcesso Situacao { get; set; }

    public ICollection<Vestigio> Vestigios { get; set; } = [];
}
