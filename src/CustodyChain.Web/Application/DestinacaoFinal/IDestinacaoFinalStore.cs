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

public sealed record ContextoSolicitacaoDestinacao(long VestigioId, string RotuloEvidencia);

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
    DateTime SolicitadoEm);

public sealed record ContextoAprovacaoDestinacao(
    long DescarteId,
    long VestigioId,
    string RotuloEvidencia,
    string Tipo,
    string DidMagistrado,
    string DidAprovador);

public sealed record AprovacaoDestinacaoPendente(
    long DescarteId,
    long AprovadorId,
    long VestigioId,
    string Tipo,
    DateTime ExecutadoEm,
    string PayloadJson,
    string PayloadHashSha256,
    string CredencialId,
    string DidResponsavel);
