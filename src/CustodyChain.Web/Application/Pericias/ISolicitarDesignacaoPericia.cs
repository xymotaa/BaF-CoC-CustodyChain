namespace CustodyChain.Web.Application.Pericias;

public interface ISolicitarDesignacaoPericia
{
    Task<ResultadoSolicitacaoDesignacaoPericia> ExecutarAsync(
        SolicitarDesignacaoPericiaCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record SolicitarDesignacaoPericiaCommand(
    long SolicitanteId,
    long? VestigioId,
    long? PeritoId,
    string? AreaPericial,
    string? Prioridade);

public sealed record ResultadoSolicitacaoDesignacaoPericia(
    long PericiaId,
    string RotuloEvidencia,
    string PeritoNome);
