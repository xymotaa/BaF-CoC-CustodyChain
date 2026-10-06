namespace CustodyChain.Web.Application.Pericias;

public interface IDesignacaoPericiaStore
{
    Task<ContextoSolicitacaoDesignacaoPericia?> ObterContextoSolicitacaoAsync(
        long vestigioId,
        long peritoId,
        long solicitanteId,
        CancellationToken cancellationToken);

    Task<long> PersistirSolicitacaoAsync(
        SolicitacaoDesignacaoPericiaPendente solicitacao,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SolicitacaoDesignacaoPericiaResumo>> ListarPendentesAsync(
        long aprovadorId,
        CancellationToken cancellationToken);

    Task<SolicitacaoDesignacaoPericiaDetalhe?> ObterDetalheAsync(
        long periciaId,
        long aprovadorId,
        CancellationToken cancellationToken);

    Task<ContextoAprovacaoDesignacaoPericia?> PrepararAprovacaoAsync(
        PreparacaoCredencialDesignacaoPericia preparacao,
        CancellationToken cancellationToken);

    Task<ContextoAprovacaoDesignacaoPericia?> ObterContextoAprovacaoAsync(
        long periciaId,
        long aprovadorId,
        CancellationToken cancellationToken);

    Task ConcluirAprovacaoAsync(
        long periciaId,
        long aprovadorId,
        string identificadorCredencial,
        DateTime concluidaEm,
        CancellationToken cancellationToken);
}

public sealed record ContextoSolicitacaoDesignacaoPericia(
    long VestigioId,
    long ProcessoId,
    string RotuloEvidencia,
    long PeritoId,
    string PeritoNome);

public sealed record SolicitacaoDesignacaoPericiaPendente(
    long VestigioId,
    long ProcessoId,
    long PeritoId,
    long SolicitanteId,
    string? AreaPericial,
    string Prioridade,
    DateTime SolicitadaEm);

public sealed record PreparacaoCredencialDesignacaoPericia(
    long PericiaId,
    long AprovadorId,
    string IdentificadorCredencial,
    DateTime EmitidaEm,
    DateTime? ValidaAte);

public sealed record ContextoAprovacaoDesignacaoPericia(
    long PericiaId,
    string IdentificadorCredencial,
    DateTime EmitidaEm,
    DateTime? ValidaAte,
    string DidEmissor,
    string DidPerito,
    long ProcessoId,
    long VestigioId,
    string RotuloEvidencia,
    string PeritoNome,
    bool Concluida);
