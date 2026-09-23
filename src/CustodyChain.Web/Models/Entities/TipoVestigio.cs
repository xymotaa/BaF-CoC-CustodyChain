namespace CustodyChain.Web.Models.Entities;

public enum CategoriaVestigio
{
    BALISTICO,
    VEICULO,
    MATERIAL_GENERICO,
    ANEXO_DIGITAL,
    DROGA,
    PADRAO_CONFRONTO,
    SUPORTE
}

public class TipoVestigio
{
    public short Id { get; set; }
    public CategoriaVestigio Categoria { get; set; }
    public required string Descricao { get; set; }
    public string? AreaPericial { get; set; }
    public bool ExigeHash { get; set; }

    public ICollection<Vestigio> Vestigios { get; set; } = [];
}
