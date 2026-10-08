namespace CustodyChain.Web.Application.CadastroVestigio;

using System.Text.Json;

public interface ICadastrarVestigio
{
    Task<PreparacaoCadastroVestigio> PrepararAsync(
        CadastrarVestigioCommand command,
        CancellationToken cancellationToken = default);
    Task<ResultadoCadastroVestigio> ExecutarAsync(ConcluirCadastroVestigioCommand command, CancellationToken cancellationToken = default);
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
    string? DescricaoIntercorrencia,
    ArquivoEvidenciaColeta? ArquivoEvidencia = null);

public sealed record ArquivoEvidenciaColeta(
    string NomeArquivo,
    string MediaType,
    byte[] Conteudo);

public sealed record ConcluirCadastroVestigioCommand(CadastrarVestigioCommand Cadastro, JsonElement OperacaoAssinada);
public sealed record PreparacaoCadastroVestigio(JsonElement Operacao, string DidColetor);

public sealed record ResultadoCadastroVestigio(
    long VestigioId,
    string RotuloEvidencia,
    bool AncoragemPendente);
