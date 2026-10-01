using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.Remessa;

public sealed class CriarRemessaUseCase(
    ICriarRemessaStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : ICriarRemessa
{
    public async Task<ResultadoCriarRemessa> ExecutarAsync(
        CriarRemessaCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(
            dados.VestigioId,
            dados.CriadorId,
            dados.DestinoId,
            cancellationToken)
            ?? throw new RecursoRemessaNaoEncontradoException(
                "Vestígio, destino ou interveniente autenticado não está disponível para esta remessa.");

        var criadoEm = clock.UtcNow;
        var payloadJson = CriarPayloadJson(dados, contexto, criadoEm);
        var payloadHashSha256 = CalcularHash(payloadJson);
        var credencialId = geradorIdentificadorCredencial.GerarCoC();

        await store.PersistirAsync(
            new RemessaPendente(
                contexto.VestigioId,
                dados.CriadorId,
                dados.DestinoId,
                dados.DataHoraSaida,
                dados.CodigoRastreamento,
                criadoEm,
                payloadJson,
                payloadHashSha256,
                credencialId,
                contexto.DidOrigem),
            cancellationToken);

        return new ResultadoCriarRemessa(contexto.RotuloEvidencia, contexto.NomeDestino, AncoragemPendente: true);
    }

    private static CriarRemessaCommand Normalizar(CriarRemessaCommand command)
    {
        if (command.CriadorId <= 0)
        {
            throw new AtorRemessaNaoAutorizadoException("A identidade autenticada é inválida.");
        }

        if (command.VestigioId <= 0)
        {
            throw new ValidacaoRemessaException("Selecione um vestígio válido.", nameof(command.VestigioId));
        }

        if (command.DestinoId <= 0)
        {
            throw new ValidacaoRemessaException("Selecione um destino válido.", nameof(command.DestinoId));
        }

        if (command.DestinoId == command.CriadorId)
        {
            throw new ValidacaoRemessaException("O destino não pode ser você mesmo.", nameof(command.DestinoId));
        }

        return command with
        {
            CodigoRastreamento = Limpar(command.CodigoRastreamento),
            DataHoraSaida = command.DataHoraSaida.ToUniversalTime(),
        };
    }

    private static string CriarPayloadJson(CriarRemessaCommand command, ContextoRemessa contexto, DateTime criadoEm)
    {
        var payload = new
        {
            RE = contexto.RotuloEvidencia,
            Origem = contexto.DidOrigem,
            Destino = contexto.DidDestino,
            command.DataHoraSaida,
            command.CodigoRastreamento,
            CriadoEm = criadoEm,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string CalcularHash(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

    private static string? Limpar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
