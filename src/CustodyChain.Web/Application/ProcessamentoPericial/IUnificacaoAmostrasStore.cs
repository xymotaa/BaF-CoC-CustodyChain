namespace CustodyChain.Web.Application.ProcessamentoPericial;

public interface IUnificacaoAmostrasStore
{
    Task<ContextoUnificacaoAmostras?> ObterContextoAsync(long periciaId, long peritoId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrigemUnificacaoAmostra>> ObterOrigensAsync(IReadOnlyList<long> vestigioIds, CancellationToken cancellationToken);
    Task<bool> RotuloEvidenciaExisteAsync(string rotuloEvidencia, CancellationToken cancellationToken);
    Task PersistirAsync(UnificacaoAmostrasPendente unificacao, CancellationToken cancellationToken);
}

public sealed record ContextoUnificacaoAmostras(long PericiaId, long VestigioPrincipalId, string DidPerito);
public sealed record OrigemUnificacaoAmostra(long VestigioId, string RotuloEvidencia, string RotuloConjunto,
    long ProcessoId, short TipoVestigioId, string? HashSha256);
public sealed record UnificacaoAmostrasPendente(long PericiaId, long PeritoId,
    IReadOnlyList<OrigemUnificacaoAmostra> Origens, string RotuloEvidenciaResultante, string DescricaoResultante,
    string Justificativa, string? HashCombinado, DateTime ExecutadoEm, string PayloadJson, string PayloadHashSha256,
    string CredencialId, string DidResponsavel);
