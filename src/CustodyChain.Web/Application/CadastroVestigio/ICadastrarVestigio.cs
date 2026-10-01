namespace CustodyChain.Web.Application.CadastroVestigio;

public interface ICadastrarVestigio
{
    Task<ResultadoCadastroVestigio> ExecutarAsync(
        CadastrarVestigioCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record CadastrarVestigioCommand(
    long CriadorId,
    string RotuloEvidencia,
    string RotuloConjunto,
    string? NumeroEvidencia,
    long ProcessoId,
    short TipoVestigioId,
    string Descricao,
    string? LocalColeta,
    DateTime DataHoraColeta,
    string? MetodoColeta,
    string NumeroLacre,
    bool HouveIntercorrencia,
    string? DescricaoIntercorrencia);

public sealed record ResultadoCadastroVestigio(
    long VestigioId,
    string RotuloEvidencia,
    bool AncoragemPendente);
