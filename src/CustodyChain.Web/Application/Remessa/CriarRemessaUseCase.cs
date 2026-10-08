using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.Remessa;

public sealed class CriarRemessaUseCase(
    ICriarRemessaStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce nonce) : ICriarRemessa
{
    public async Task<PreparacaoRemessa> PrepararAsync(
        CriarRemessaCommand command,
        CancellationToken cancellationToken = default)
    {
        var remessa = Normalizar(command);
        var contexto = await ObterContextoAsync(remessa, cancellationToken);
        return new PreparacaoRemessa(CriarOperacao(remessa, contexto), contexto.DidOrigem);
    }

    public async Task<ResultadoCriarRemessa> ExecutarAsync(
        ConcluirRemessaCommand command,
        CancellationToken cancellationToken = default)
    {
        var remessa = Normalizar(command.Remessa);
        var contexto = await ObterContextoAsync(remessa, cancellationToken);
        var operacao = LerOperacaoAssinada(command.OperacaoAssinada);

        ValidarVinculoDaOperacao(operacao, remessa, contexto);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);
        await store.PersistirAsync(CriarRemessaConfirmada(remessa, contexto, operacao), cancellationToken);

        return new ResultadoCriarRemessa(contexto.RotuloEvidencia, contexto.NomeDestino, AncoragemPendente: false);
    }

    private async Task<ContextoRemessa> ObterContextoAsync(
        CriarRemessaCommand remessa,
        CancellationToken cancellationToken) =>
        await store.ObterContextoAsync(remessa.VestigioId, remessa.CriadorId, remessa.DestinoId, cancellationToken)
        ?? throw new RecursoRemessaNaoEncontradoException(
            "Vestígio, coleta confirmada, coletor ou custódia inicial não está disponível para esta remessa.");

    private JsonElement CriarOperacao(CriarRemessaCommand remessa, ContextoRemessa contexto)
    {
        var agora = Agora();
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = "REMESSA_CRIAR",
            payload = new
            {
                transferType = "INICIAL",
                assetRef = contexto.AssetRef,
                assetId = contexto.VestigioId.ToString(),
                processoId = contexto.ProcessoId.ToString(),
                origemDid = contexto.DidOrigem,
                destinoDid = contexto.DidDestino,
                dataHoraSaida = remessa.DataHoraSaida.ToString("O"),
                codigoRastreamento = remessa.CodigoRastreamento,
                coletaOperationId = contexto.ColetaOperationId
            },
            signerDid = contexto.DidOrigem,
            keyId = $"{contexto.DidOrigem}#key-1",
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            timestamp = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"),
            nonce = Convert.ToBase64String(nonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacaoAssinada(JsonElement operacaoAssinada)
    {
        if (operacaoAssinada.ValueKind != JsonValueKind.Object)
            throw new ValidacaoRemessaException("Informe a operação assinada pela wallet.");

        try { return OperacaoAssinadaV1.Ler(operacaoAssinada.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoRemessaException(exception.Message);
        }
    }

    private static void ValidarVinculoDaOperacao(
        OperacaoAssinadaV1 operacao,
        CriarRemessaCommand remessa,
        ContextoRemessa contexto)
    {
        var envelope = operacao.Envelope;
        var payload = envelope.GetProperty("payload");
        var corresponde = Texto(envelope, "operation", "REMESSA_CRIAR")
            && Texto(envelope, "signerDid", contexto.DidOrigem)
            && Texto(payload, "transferType", "INICIAL")
            && Texto(payload, "assetRef", contexto.AssetRef)
            && Texto(payload, "assetId", contexto.VestigioId.ToString())
            && Texto(payload, "processoId", contexto.ProcessoId.ToString())
            && Texto(payload, "origemDid", contexto.DidOrigem)
            && Texto(payload, "destinoDid", contexto.DidDestino)
            && Texto(payload, "dataHoraSaida", remessa.DataHoraSaida.ToString("O"))
            && TextoOuNulo(payload, "codigoRastreamento", remessa.CodigoRastreamento)
            && Texto(payload, "coletaOperationId", contexto.ColetaOperationId);

        if (!corresponde)
            throw new ValidacaoRemessaException(
                "A operação assinada não corresponde à coleta ou à remessa inicial disponível.");
    }

    private async Task ConfirmarNoLedgerAsync(OperacaoAssinadaV1 operacao, CancellationToken cancellationToken)
    {
        try
        {
            var operationId = await ledger.RegistrarOperacaoAssinadaV1Async(
                new OperacaoAssinadaV1Dto(operacao.Envelope), cancellationToken);
            if (!string.Equals(operationId, operacao.OperationId, StringComparison.Ordinal))
                throw new InvalidOperationException("O ledger confirmou uma operação diferente da solicitada.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new IndisponibilidadeLedgerRemessaException(
                "Não foi possível confirmar a remessa inicial no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private RemessaConfirmada CriarRemessaConfirmada(
        CriarRemessaCommand remessa,
        ContextoRemessa contexto,
        OperacaoAssinadaV1 operacao) =>
        new(
            contexto.VestigioId,
            remessa.CriadorId,
            remessa.DestinoId,
            remessa.DataHoraSaida,
            remessa.CodigoRastreamento,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            Agora(),
            operacao.Envelope.GetRawText(),
            operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidOrigem);

    private static CriarRemessaCommand Normalizar(CriarRemessaCommand command)
    {
        if (command.CriadorId <= 0)
            throw new AtorRemessaNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.VestigioId <= 0)
            throw new ValidacaoRemessaException("Selecione um vestígio válido.", nameof(command.VestigioId));
        if (command.DestinoId <= 0)
            throw new ValidacaoRemessaException("Selecione um destino válido.", nameof(command.DestinoId));
        if (command.DestinoId == command.CriadorId)
            throw new ValidacaoRemessaException("O destino não pode ser você mesmo.", nameof(command.DestinoId));

        return command with
        {
            CodigoRastreamento = Limpar(command.CodigoRastreamento),
            DataHoraSaida = command.DataHoraSaida.ToUniversalTime()
        };
    }

    private static bool Texto(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && valor.GetString() == esperado;

    private static bool TextoOuNulo(JsonElement objeto, string nome, string? esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && (esperado is null ? valor.ValueKind == JsonValueKind.Null : Texto(objeto, nome, esperado));

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
