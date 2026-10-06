using System.Text.Json;

namespace CustodyChain.Web.Application.Pericias;

public interface IAprovarDesignacaoPericia
{
    Task<IReadOnlyList<SolicitacaoDesignacaoPericiaResumo>> ListarPendentesAsync(
        long aprovadorId,
        CancellationToken cancellationToken = default);

    Task<SolicitacaoDesignacaoPericiaDetalhe> ObterDetalheAsync(
        long periciaId,
        long aprovadorId,
        CancellationToken cancellationToken = default);

    Task<PreparacaoAprovacaoDesignacaoPericia> PrepararAsync(
        PrepararAprovacaoDesignacaoPericiaCommand command,
        CancellationToken cancellationToken = default);

    Task<ResultadoAprovacaoDesignacaoPericia> ConcluirAsync(
        ConcluirAprovacaoDesignacaoPericiaCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record SolicitacaoDesignacaoPericiaResumo(
    long PericiaId,
    string RotuloEvidencia,
    string PeritoNome,
    string? AreaPericial,
    string Prioridade,
    DateTime SolicitadaEm);

public sealed record SolicitacaoDesignacaoPericiaDetalhe(
    long PericiaId,
    string RotuloEvidencia,
    string DescricaoVestigio,
    string PeritoNome,
    string DidPerito,
    string? AreaPericial,
    string Prioridade,
    DateTime SolicitadaEm);

public sealed record PrepararAprovacaoDesignacaoPericiaCommand(
    long PericiaId,
    long AprovadorId,
    DateTime? ValidaAte);

public sealed record PreparacaoAprovacaoDesignacaoPericia(
    JsonElement Credencial,
    string DidEmissor,
    string RotuloEvidencia);

public sealed record ConcluirAprovacaoDesignacaoPericiaCommand(
    long PericiaId,
    long AprovadorId,
    JsonElement Credencial);

public sealed record ResultadoAprovacaoDesignacaoPericia(
    long PericiaId,
    string RotuloEvidencia,
    string PeritoNome);
