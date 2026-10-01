using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class EmitirLaudoUseCase(
    IEmissaoLaudoStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IEmitirLaudo
{
    public async Task<ResultadoEmissaoLaudo> ExecutarAsync(
        EmitirLaudoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(dados.PericiaId, dados.PeritoId, cancellationToken)
            ?? throw new RecursoEmissaoLaudoNaoEncontradoException(
                "Perícia não encontrada ou lacre ainda não rompido.");
        if (string.IsNullOrEmpty(contexto.HashVestigios))
            throw new HashVestigioAusenteException(
                "Vestígio sem hash SHA-256 registrado (RN13) — não é possível vincular o laudo.");

        var emitidoEm = clock.UtcNow;
        var hashLaudo = CalcularHash(dados.Conteudo! + contexto.HashVestigios);
        var numeroLaudo = $"LAUDO-{emitidoEm:yyyy}-{contexto.PericiaId:D6}";
        var payloadJson = JsonSerializer.Serialize(new
        {
            RE = contexto.RotuloEvidencia,
            NumeroLaudo = numeroLaudo,
            Versao = 1,
            HashVestigios = contexto.HashVestigios,
            HashLaudo = hashLaudo,
            EmitidoPor = contexto.DidPerito,
            EmitidoEm = emitidoEm,
        });

        await store.PersistirAsync(new LaudoPendente(
            contexto.PericiaId,
            dados.PeritoId,
            numeroLaudo,
            dados.Conteudo!,
            contexto.HashVestigios,
            hashLaudo,
            emitidoEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoEmissaoLaudo(contexto.RotuloEvidencia, numeroLaudo, AncoragemPendente: true);
    }

    private static EmitirLaudoCommand Normalizar(EmitirLaudoCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorEmissaoLaudoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoEmissaoLaudoException("Perícia inválida.", nameof(command.PericiaId));
        if (string.IsNullOrWhiteSpace(command.Conteudo))
            throw new ValidacaoEmissaoLaudoException("Informe o conteúdo do laudo.", nameof(command.Conteudo));

        return command with { Conteudo = command.Conteudo.Trim() };
    }

    private static string CalcularHash(string conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));
}
