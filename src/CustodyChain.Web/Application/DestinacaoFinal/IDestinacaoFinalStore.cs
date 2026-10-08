namespace CustodyChain.Web.Application.DestinacaoFinal;

public interface IDestinacaoFinalStore
{
    Task<ContextoSolicitacaoDestinacao?> ObterContextoSolicitacaoAsync(
        long vestigioId,
        long solicitanteId,
        CancellationToken cancellationToken);

    Task PersistirSolicitacaoAsync(SolicitacaoDestinacaoPendente solicitacao, CancellationToken cancellationToken);

    Task<ContextoAprovacaoDestinacao?> ObterContextoAprovacaoAsync(
        long descarteId,
        long aprovadorId,
        CancellationToken cancellationToken);

    Task PersistirAprovacaoAsync(AprovacaoDestinacaoPendente aprovacao, CancellationToken cancellationToken);
}

public sealed record ContextoSolicitacaoDestinacao(
    long VestigioId,
    long ProcessoId,
    string AssetRef,
    string RotuloEvidencia,
    string DidSolicitante,
    string CredencialId,
    string GuardaOperationId);

public sealed record SolicitacaoDestinacaoPendente(
    long VestigioId,
    long SolicitanteId,
    string Tipo,
    string DidMagistrado,
    string NomeArquivoAutorizacao,
    string CidAutorizacao,
    long TamanhoBytesAutorizacao,
    string HashAutorizacao,
    string? Observacao,
    DateTime SolicitadoEm,
    string CredencialId,
    string GuardaOperationId,
    string OperacaoAssinadaId,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel);

public sealed record ContextoAprovacaoDestinacao(
    long DescarteId,
    long VestigioId,
    long ProcessoId,
    string AssetRef,
    string RotuloEvidencia,
    string Tipo,
    string CidAutorizacao,
    string HashAutorizacao,
    string SolicitacaoOperationId,
    string DidSolicitante,
    string DidAprovador);

public sealed record AprovacaoDestinacaoPendente(
    long DescarteId,
    long AprovadorId,
    long VestigioId,
    string Tipo,
    DateTime ExecutadoEm,
    string OperacaoAssinadaId,
    string OperacaoAssinadaJson,
    string OperacaoAssinadaHashSha256,
    string DidResponsavel,
    string SolicitacaoOperationId);
