using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class RomperLacreUseCase(
    IRompimentoLacreStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IRomperLacre
{
    public async Task<ResultadoRompimentoLacre> ExecutarAsync(
        RomperLacreCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var rompidoEm = clock.UtcNow;
        var contexto = await store.ObterContextoAsync(dados.PericiaId, dados.PeritoId, rompidoEm, cancellationToken)
            ?? throw new RecursoRompimentoLacreNaoEncontradoException(
                "Perícia não encontrada ou vestígio ainda não recebido.");

        if (!contexto.CredencialValida)
            throw new CredencialPermissaoInvalidaException(
                "Credencial de permissão inválida, revogada ou expirada para este vestígio (RN10).");
        if (contexto.LacreId is null || contexto.NumeroLacre is null)
            throw new LacreIntactoNaoEncontradoException("Nenhum lacre intacto encontrado para este vestígio.");

        var payloadJson = JsonSerializer.Serialize(new
        {
            RE = contexto.RotuloEvidencia,
            NumeroLacreRompido = contexto.NumeroLacre,
            dados.Justificativa,
            RompidoPor = contexto.DidPerito,
            RompidoEm = rompidoEm,
        });

        await store.PersistirAsync(new RompimentoLacrePendente(
            contexto.PericiaId,
            dados.PeritoId,
            contexto.LacreId.Value,
            contexto.NumeroLacre,
            dados.Justificativa!,
            rompidoEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoRompimentoLacre(contexto.RotuloEvidencia, contexto.NumeroLacre, AncoragemPendente: true);
    }

    private static RomperLacreCommand Normalizar(RomperLacreCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorRompimentoLacreNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoRompimentoLacreException("Perícia inválida.", nameof(command.PericiaId));
        if (string.IsNullOrWhiteSpace(command.Justificativa))
            throw new ValidacaoRompimentoLacreException(
                "Informe a justificativa do rompimento.", nameof(command.Justificativa));

        return command with { Justificativa = command.Justificativa.Trim() };
    }

    private static string CalcularHash(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
}
