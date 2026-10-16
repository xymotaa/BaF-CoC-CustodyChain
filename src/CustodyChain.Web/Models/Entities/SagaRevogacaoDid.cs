namespace CustodyChain.Web.Models.Entities;

public enum EstadoSagaRevogacaoDid
{
    PENDENTE_LEDGER,
    PROCESSANDO_LEDGER,
    CONFIRMADA_NO_LEDGER,
    FALHA_RECONCILIACAO,
    FALHA_PERMANENTE
}

/// <summary>Registro durável para retomar a mesma revogação assinada no Fabric.</summary>
public class SagaRevogacaoDid
{
    public long Id { get; set; }
    public required string CommandId { get; set; }
    public long IntervenienteId { get; set; }
    public long AdministradorId { get; set; }
    public required string DidAlvo { get; set; }
    public required string CommandJson { get; set; }
    public required string KeyId { get; set; }
    public required string Signature { get; set; }
    public EstadoSagaRevogacaoDid Estado { get; set; }
    public byte Tentativas { get; set; }
    public string? Erro { get; set; }
    public DateTime CriadaEm { get; set; }
    public DateTime? UltimaTentativaEm { get; set; }
    public DateTime? ConfirmadaEm { get; set; }
    public DateTime? ProximaTentativaEm { get; set; }

    public Interveniente Interveniente { get; set; } = null!;
    public Interveniente Administrador { get; set; } = null!;
}
