namespace CustodyChain.Web.Models.Entities;

public enum EstadoRegistroLedger
{
    PENDENTE,
    ANCORADO,
    FALHA
}

/// <summary>
/// entidade_origem + registro_origem_id formam uma FK polimórfica que não
/// pode ser declarada no MySQL (Seção 8.4, decisão 2 do documento técnico).
/// A integridade fica a cargo da aplicação; a unicidade sobre
/// (entidade_origem, registro_origem_id, evento) garante idempotência.
/// </summary>
public class RegistroLedger
{
    public long Id { get; set; }
    public required string EntidadeOrigem { get; set; }
    public long RegistroOrigemId { get; set; }
    public long? VestigioId { get; set; }
    public required string Evento { get; set; }
    public string? PayloadJson { get; set; }
    public EstadoRegistroLedger Estado { get; set; }
    public byte Tentativas { get; set; }
    public string? TxHash { get; set; }
    public long? Bloco { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime? AncoradoEm { get; set; }

    public Vestigio? Vestigio { get; set; }
}
