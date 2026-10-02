using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.DestinacaoFinal;

public sealed class AprovarDestinacaoUseCase(
    IDestinacaoFinalStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IAprovarDestinacao
{
    public async Task<ResultadoAprovacaoDestinacao> ExecutarAsync(
        AprovarDestinacaoCommand command,
        CancellationToken cancellationToken = default)
    {
        Validar(command);
        var contexto = await store.ObterContextoAprovacaoAsync(
            command.DescarteId,
            command.AprovadorId,
            cancellationToken) ?? throw new RecursoDestinacaoFinalNaoEncontradoException(
                "Destinação não encontrada, ou quem solicitou não pode aprovar (RN16).");

        var executadoEm = clock.UtcNow;
        var payloadJson = JsonSerializer.Serialize(new
        {
            RE = contexto.RotuloEvidencia,
            Tipo = contexto.Tipo,
            contexto.DidMagistrado,
            AprovadoPor = contexto.DidAprovador,
            ExecutadoEm = executadoEm,
        });

        await store.PersistirAprovacaoAsync(new AprovacaoDestinacaoPendente(
            contexto.DescarteId,
            command.AprovadorId,
            contexto.VestigioId,
            contexto.Tipo,
            executadoEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidAprovador), cancellationToken);

        return new ResultadoAprovacaoDestinacao(contexto.Tipo, contexto.RotuloEvidencia, AncoragemPendente: true);
    }

    private static void Validar(AprovarDestinacaoCommand command)
    {
        if (command.AprovadorId <= 0)
            throw new AtorDestinacaoFinalNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.DescarteId <= 0)
            throw new ValidacaoDestinacaoFinalException("Destinação inválida.", nameof(command.DescarteId));
    }

    private static string CalcularHash(string conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));
}
