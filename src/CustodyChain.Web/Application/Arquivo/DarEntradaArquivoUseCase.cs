using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.Arquivo;

public sealed class DarEntradaArquivoUseCase(
    IEntradaArquivoStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IDarEntradaArquivo
{
    public async Task<ResultadoDarEntradaArquivo> ExecutarAsync(
        DarEntradaArquivoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(dados.VestigioId, dados.RecebedorId, cancellationToken)
            ?? throw new RecursoEntradaArquivoNaoEncontradoException(
                "Vestígio não encontrado, não está com você, ou não está mais no estado Recebido.");

        var entradaEm = clock.UtcNow;
        var payloadJson = JsonSerializer.Serialize(new
        {
            RE = contexto.RotuloEvidencia,
            dados.Central,
            dados.Posicao,
            dados.PrazoGuardaAte,
            Responsavel = contexto.DidRecebedor,
            EntradaEm = entradaEm,
        });

        await store.PersistirAsync(new EntradaArquivoPendente(
            contexto.VestigioId,
            dados.RecebedorId,
            dados.Central!,
            dados.Posicao,
            dados.PrazoGuardaAte,
            entradaEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidRecebedor), cancellationToken);

        return new ResultadoDarEntradaArquivo(contexto.RotuloEvidencia, AncoragemPendente: true);
    }

    private static DarEntradaArquivoCommand Normalizar(DarEntradaArquivoCommand command)
    {
        if (command.RecebedorId <= 0)
            throw new AtorEntradaArquivoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.VestigioId <= 0)
            throw new ValidacaoEntradaArquivoException("Selecione o vestígio.", nameof(command.VestigioId));
        if (string.IsNullOrWhiteSpace(command.Central))
            throw new ValidacaoEntradaArquivoException("Informe a central de custódia.", nameof(command.Central));

        return command with
        {
            Central = command.Central.Trim(),
            Posicao = Limpar(command.Posicao),
        };
    }

    private static string CalcularHash(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
