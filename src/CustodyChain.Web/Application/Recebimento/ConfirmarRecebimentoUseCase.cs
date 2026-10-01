using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.Recebimento;

public sealed class ConfirmarRecebimentoUseCase(
    IRecebimentoStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IConfirmarRecebimento
{
    public async Task<ResultadoConfirmarRecebimento> ExecutarAsync(
        ConfirmarRecebimentoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(dados.MovimentacaoId, dados.DestinoId, cancellationToken)
            ?? throw new RecursoRecebimentoNaoEncontradoException(
                "Recebimento pendente ou interveniente autenticado não está disponível.");

        var recebidoEm = clock.UtcNow;
        var lacreConferido = dados.NumeroLacreConferido!;
        var lacreConfere = contexto.NumeroLacreEsperado is not null
            && contexto.NumeroLacreEsperado == lacreConferido;
        var evento = lacreConfere ? "RECEBIMENTO" : "ROMPIMENTO";
        var resultado = lacreConfere ? "lacre_conferido" : "divergencia_lacre";
        var payloadJson = JsonSerializer.Serialize(new
        {
            RE = contexto.RotuloEvidencia,
            NumeroLacreEsperado = contexto.NumeroLacreEsperado,
            NumeroLacreConferido = lacreConferido,
            Resultado = resultado,
            Recebedor = contexto.DidDestino,
            RecebidoEm = recebidoEm,
        });

        await store.ConfirmarAsync(new RecebimentoConfirmadoPendente(
            contexto.MovimentacaoId,
            dados.DestinoId,
            contexto.NumeroLacreEsperado,
            lacreConferido,
            lacreConfere,
            evento,
            recebidoEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidDestino), cancellationToken);

        return new ResultadoConfirmarRecebimento(contexto.RotuloEvidencia, lacreConfere, AncoragemPendente: true);
    }

    private static ConfirmarRecebimentoCommand Normalizar(ConfirmarRecebimentoCommand command)
    {
        if (command.DestinoId <= 0)
            throw new AtorRecebimentoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.MovimentacaoId <= 0)
            throw new ValidacaoRecebimentoException("Recebimento inválido.", nameof(command.MovimentacaoId));
        if (string.IsNullOrWhiteSpace(command.NumeroLacreConferido))
            throw new ValidacaoRecebimentoException("Informe o número do lacre conferido.", nameof(command.NumeroLacreConferido));

        return command with { NumeroLacreConferido = command.NumeroLacreConferido!.Trim() };
    }

    private static string CalcularHash(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
}
