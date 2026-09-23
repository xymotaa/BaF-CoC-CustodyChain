namespace CustodyChain.Web.Models.Entities;

public enum FaseVestigio
{
    EXTERNA,
    INTERNA
}

public enum EstadoVestigio
{
    Reconhecido,
    Coletado,
    EmTransporte,
    Recebido,
    CustodiaComprometida,
    EmPericia,
    Armazenado,
    Periciado,
    Descartado
}

public class Vestigio
{
    public long Id { get; set; }
    public required string RotuloEvidencia { get; set; }
    public required string RotuloConjunto { get; set; }
    public string? NumeroEvidencia { get; set; }
    public long ProcessoId { get; set; }
    public short TipoVestigioId { get; set; }
    public required string Descricao { get; set; }
    public long? CriadorId { get; set; }
    public long? CustodianteAtualId { get; set; }
    public string? LocalColeta { get; set; }
    public DateTime? DataHoraColeta { get; set; }
    public string? MetodoColeta { get; set; }
    public bool? HouveIntercorrencia { get; set; }
    public string? DescricaoIntercorrencia { get; set; }

    /// <summary>
    /// Hash SHA-256 do payload do Lacre Digital submetido na coleta.
    /// Extensão deste projeto (não listada no dicionário de dados oficial,
    /// Seção 8.2): sem ela, a busca por hash de T-06 e o verificador
    /// independente de T-11 não teriam onde consultar — o hash existiria
    /// só dentro do texto de REGISTRO_LEDGER.PayloadJson.
    /// </summary>
    public string? HashSha256 { get; set; }

    public byte EtapaAtual { get; set; }
    public FaseVestigio FaseAtual { get; set; }
    public EstadoVestigio Estado { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AtualizadoEm { get; set; }

    public Processo Processo { get; set; } = null!;
    public TipoVestigio TipoVestigio { get; set; } = null!;
    public Interveniente? Criador { get; set; }
    public Interveniente? CustodianteAtual { get; set; }
}
