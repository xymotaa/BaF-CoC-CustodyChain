using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;

namespace CustodyChain.Web.Application.RevogacaoDid;

public interface IRevogarDid
{
    Task<PreparacaoRevogacaoDid> PrepararAsync(PrepararRevogacaoDidCommand command, CancellationToken cancellationToken = default);
    Task<ResultadoRevogacaoDid> ExecutarAsync(ExecutarRevogacaoDidCommand command, CancellationToken cancellationToken = default);
}

public interface IReconciliarRevogacaoDid
{
    Task<int> ExecutarLoteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Porta de persistência: a Application não conhece EF Core nem suas entidades.</summary>
public interface IRevogacaoDidStore
{
    Task<ContextoRevogacaoDid?> ObterContextoAsync(long intervenienteId, CancellationToken cancellationToken = default);
    Task<SagaRevogacaoDidDto?> ObterPorCommandIdAsync(string commandId, CancellationToken cancellationToken = default);
    Task RegistrarRejeicaoAsync(RejeicaoRevogacaoDid rejeicao, CancellationToken cancellationToken = default);
    Task<SagaRevogacaoDidDto> CriarOuObterAsync(NovaSagaRevogacaoDid saga, CancellationToken cancellationToken = default);
    Task<SagaRevogacaoDidDto?> TentarAssumirAsync(string commandId, DateTime agora, TimeSpan lease, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SagaRevogacaoDidDto>> AssumirPendenciasAsync(DateTime agora, TimeSpan lease, int limite, CancellationToken cancellationToken = default);
    Task ConfirmarAsync(string commandId, DateTime confirmadoEm, bool reconciliada, CancellationToken cancellationToken = default);
    Task RegistrarFalhaTransitoriaAsync(string commandId, string erro, DateTime agora, DateTime proximaTentativaEm, CancellationToken cancellationToken = default);
    Task RegistrarFalhaDefinitivaAsync(string commandId, string erro, DateTime agora, CancellationToken cancellationToken = default);
}

public interface IRevogacaoDidLedger
{
    Task<GovernancaRevogacaoDid> ObterGovernancaAsync(CancellationToken cancellationToken = default);
    Task RevogarAsync(string did, ProvaRevogacaoDid prova, CancellationToken cancellationToken = default);
}

public sealed record PrepararRevogacaoDidCommand(long IntervenienteId, long AdministradorId, string DidAdministrador, ContextoAuditoriaRevogacaoDid Auditoria);
public sealed record PreparacaoRevogacaoDid(JsonElement Command);
public sealed record ExecutarRevogacaoDidCommand(
    long IntervenienteId,
    long AdministradorId,
    string DidAdministrador,
    JsonElement Command,
    string KeyId,
    string Signature,
    ContextoAuditoriaRevogacaoDid Auditoria);
public sealed record ContextoAuditoriaRevogacaoDid(string? Ip, string? UserAgent);
public sealed record ContextoRevogacaoDid(long IntervenienteId, long AdministradorId, string DidAlvo, string NomeAlvo, bool Ativo, bool PossuiResponsabilidadesPendentes);
public sealed record GovernancaRevogacaoDid(int Revisao, IReadOnlyList<string> AdministradoresAtivos);
public sealed record ProvaRevogacaoDid(JsonElement Command, string KeyId, string Signature);
public sealed record NovaSagaRevogacaoDid(
    string CommandId, long IntervenienteId, long AdministradorId, string DidAlvo, string CommandJson,
    string KeyId, string Signature, ContextoAuditoriaRevogacaoDid Auditoria, DateTime CriadaEm);
public sealed record SagaRevogacaoDidDto(
    string CommandId, string DidAlvo, string CommandJson, string KeyId, string Signature,
    EstadoRevogacaoDid Estado, string NomeAlvo);
public sealed record ResultadoRevogacaoDid(string NomeAlvo, EstadoRevogacaoDid Estado);
public sealed record RejeicaoRevogacaoDid(long IntervenienteId, long AdministradorId, MotivoRejeicaoRevogacaoDid Motivo, ContextoAuditoriaRevogacaoDid Auditoria, DateTime OcorridaEm);

public enum EstadoRevogacaoDid { PendenteLedger, ProcessandoLedger, ConfirmadaNoLedger, FalhaReconciliacao, FalhaPermanente }

public sealed class RevogacaoDidInvalidaException(string message) : InvalidOperationException(message);
public sealed class RevogacaoDidRejeitadaException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);
public sealed class LedgerRevogacaoDidIndisponivelException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);
