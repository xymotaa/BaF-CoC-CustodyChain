using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.Recebimento;

public sealed class RecusarRecebimentoUseCase(
    IRecebimentoStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IRecusarRecebimento
{
    public async Task<ResultadoRecusarRecebimento> ExecutarAsync(
        RecusarRecebimentoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(dados.MovimentacaoId, dados.DestinoId, cancellationToken)
            ?? throw new RecursoRecebimentoNaoEncontradoException(
                "Recebimento pendente ou interveniente autenticado não está disponível.");

        var recusadoEm = clock.UtcNow;
        var motivoRecusa = dados.MotivoRecusa!;
        var payloadJson = JsonSerializer.Serialize(new
        {
            RE = contexto.RotuloEvidencia,
            MotivoRecusa = motivoRecusa,
            RecusadoPor = contexto.DidDestino,
            RecusadoEm = recusadoEm,
        });

        await store.RecusarAsync(new RecebimentoRecusadoPendente(
            contexto.MovimentacaoId,
            dados.DestinoId,
            motivoRecusa,
            recusadoEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidDestino), cancellationToken);

        return new ResultadoRecusarRecebimento(contexto.RotuloEvidencia, AncoragemPendente: true);
    }

    private static RecusarRecebimentoCommand Normalizar(RecusarRecebimentoCommand command)
    {
        if (command.DestinoId <= 0)
            throw new AtorRecebimentoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.MovimentacaoId <= 0)
            throw new ValidacaoRecebimentoException("Recebimento inválido.", nameof(command.MovimentacaoId));
        if (string.IsNullOrWhiteSpace(command.MotivoRecusa))
            throw new ValidacaoRecebimentoException("Informe o motivo da recusa.", nameof(command.MotivoRecusa));

        return command with { MotivoRecusa = command.MotivoRecusa!.Trim() };
    }

    private static string CalcularHash(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
}
