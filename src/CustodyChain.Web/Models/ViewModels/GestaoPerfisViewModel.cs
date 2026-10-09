using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CustodyChain.Web.Models.ViewModels;

public class CadastrarIntervenienteViewModel
{
    [Required(ErrorMessage = "Informe o nome.")]
    [StringLength(120)]
    [Display(Name = "Nome")]
    public string? Nome { get; set; }

    [StringLength(30)]
    [Display(Name = "Matrícula")]
    public string? Matricula { get; set; }

    [StringLength(80)]
    [Display(Name = "Órgão")]
    public string? Orgao { get; set; }

    [StringLength(80)]
    [Display(Name = "Lotação")]
    public string? Lotacao { get; set; }

    [Required(ErrorMessage = "Selecione o perfil.")]
    [Display(Name = "Perfil")]
    public byte? PerfilId { get; set; }

    public IReadOnlyList<OpcaoPerfilViewModel> PerfisDisponiveis { get; set; } = [];
}

public record OpcaoPerfilViewModel(byte Id, string Codigo, string Nome);

public record ItemIntervenienteViewModel(
    long Id,
    string Did,
    string Nome,
    string PerfilNome,
    string Situacao,
    string SituacaoIdentidadeLedger,
    DateTime CriadoEm,
    DateTime? AtivadoEm);

public class GestaoPerfisListaViewModel
{
    public IReadOnlyList<ItemIntervenienteViewModel> Intervenientes { get; set; } = [];
}

public record InscricaoDidCriadaViewModel(
    string Did,
    string EnrollmentId,
    string CodigoInscricao,
    DateTime ExpiraEm);

public record SolicitarComandoRegistroDidRequest(string CodigoInscricao, string VerificationMethodId, string PublicKeyMultibase);

public record EnviarProvaRegistroDidRequest(object Command, string Signature);

public record EnviarProvaAtivacaoDidRequest(object Command, string KeyId, string Signature);

public record AtivarDidV2ViewModel(long IntervenienteId, string Did, string Nome, string DidAdministrador, string WalletEndpoint);

public class EmitirCredencialPermissaoViewModel
{
    [Required(ErrorMessage = "Selecione o titular.")]
    [Display(Name = "Titular")]
    public long? TitularId { get; set; }

    [Required(ErrorMessage = "Selecione o processo.")]
    [Display(Name = "Processo")]
    public long? ProcessoId { get; set; }

    [Display(Name = "Vestígio (obrigatório para Custódia)")]
    public long? VestigioId { get; set; }

    [Display(Name = "Válida até (opcional)")]
    public DateTime? ValidaAte { get; set; }

    public IReadOnlyList<ItemIntervenienteViewModel> TitularesDisponiveis { get; set; } = [];
    public IReadOnlyList<OpcaoProcessoViewModel> ProcessosDisponiveis { get; set; } = [];
    public IReadOnlyList<OpcaoVestigioPermissaoViewModel> VestigiosDisponiveis { get; set; } = [];
}

public record OpcaoVestigioPermissaoViewModel(long Id, long ProcessoId, string RotuloEvidencia);

public record EnviarProvaVcPermissaoRequest(string EmissaoId, System.Text.Json.JsonElement Credential);

public record ItemCredencialPermissaoViewModel(long Id, string Identificador, string TitularNome, string Situacao, DateTime EmitidaEm, DateTime? ValidaAte);

public class GestaoCredenciaisViewModel
{
    public IReadOnlyList<ItemCredencialPermissaoViewModel> Credenciais { get; set; } = [];
}

public record RevogarCredencialViewModel(long Id, string Identificador, string TitularNome, string EmissorDid, string WalletEndpoint);

public record EnviarProvaRevogacaoVcRequest(object Command, string KeyId, string Signature);

public record RotacionarChaveDidViewModel(
    string Did,
    string KeyIdAtual,
    int DocumentVersion,
    string WalletEndpoint);

public record CriarComandoRotacaoDidRequest(string KeyId, string PublicKeyMultibase);

public record ProvaChaveDidDto(string KeyId, string Algorithm, string Signature);

public record EnviarProvaRotacaoDidRequest(
    JsonElement Command,
    ProvaChaveDidDto CurrentKeyProof,
    ProvaChaveDidDto NewKeyProof);

public record EnviarProvaRecuperacaoDidRequest(
    JsonElement Command,
    string AdminKeyId,
    string AdminSignature,
    string CandidateSignature);

public record RecuperarIdentidadeViewModel(string WalletEndpoint);

public record CriarPedidoRecuperacaoDidRequest(
    string Did,
    string KeyId,
    string PublicKeyMultibase);

public record CriarComandoRecuperacaoDidRequest(
    JsonElement RecoveryRequest,
    string Reason);
