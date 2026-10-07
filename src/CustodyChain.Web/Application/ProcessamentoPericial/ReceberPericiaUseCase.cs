using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class ReceberPericiaUseCase(
    IRecebimentoPericiaStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce geradorNonce) : IReceberPericia
{
    public async Task<PreparacaoRecebimentoPericia> PrepararAsync(
        ReceberPericiaCommand command,
        CancellationToken cancellationToken = default)
    {
        Validar(command);
        var recebidoEm = Agora();
        var contexto = await ObterContextoAsync(command, recebidoEm, cancellationToken);
        return new PreparacaoRecebimentoPericia(
            CriarOperacao(contexto, recebidoEm), contexto.DidPerito, contexto.RotuloEvidencia);
    }

    public async Task<ResultadoRecebimentoPericia> ExecutarAsync(
        ConcluirRecebimentoPericiaCommand command,
        CancellationToken cancellationToken = default)
    {
        var recebimento = new ReceberPericiaCommand(command.PeritoId, command.PericiaId);
        Validar(recebimento);
        var contexto = await ObterContextoAsync(recebimento, Agora(), cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, contexto);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);

        await store.PersistirAsync(new RecebimentoPericiaConfirmado(
            contexto.PericiaId,
            command.PeritoId,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            Agora(),
            operacao.Envelope.GetRawText(),
            operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoRecebimentoPericia(contexto.RotuloEvidencia);
    }

    private async Task<ContextoRecebimentoPericia> ObterContextoAsync(
        ReceberPericiaCommand command,
        DateTime agora,
        CancellationToken cancellationToken) =>
        await store.ObterContextoAsync(command.PericiaId, command.PeritoId, agora, cancellationToken)
            ?? throw new RecursoRecebimentoPericiaNaoEncontradoException(
                "Perícia não encontrada, sem permissão vigente ou já recebida.");

    private JsonElement CriarOperacao(ContextoRecebimentoPericia contexto, DateTime recebidoEm) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = "PERICIA_RECEBER",
            payload = new
            {
                credentialId = contexto.CredencialId,
                processoId = contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                periciaId = contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                assetId = contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            signerDid = contexto.DidPerito,
            keyId = $"{contexto.DidPerito}#key-1",
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            timestamp = recebidoEm.ToString("O"),
            expiresAt = recebidoEm.AddMinutes(5).ToString("O"),
            nonce = Convert.ToBase64String(geradorNonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoRecebimentoPericiaException("Informe a operação de recebimento assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoRecebimentoPericiaException(exception.Message);
        }
    }

    private static void ValidarOperacao(OperacaoAssinadaV1 operacao, ContextoRecebimentoPericia contexto)
    {
        var envelope = operacao.Envelope;
        var payload = envelope.GetProperty("payload");
        if (envelope.GetProperty("operation").GetString() != "PERICIA_RECEBER"
            || envelope.GetProperty("signerDid").GetString() != contexto.DidPerito
            || envelope.GetProperty("keyId").GetString() != $"{contexto.DidPerito}#key-1"
            || !CampoIgual(payload, "credentialId", contexto.CredencialId)
            || !CampoIgual(payload, "processoId", contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "periciaId", contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "assetId", contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            throw new ValidacaoRecebimentoPericiaException(
                "A operação assinada não corresponde à perícia ou à permissão vigente.");
    }

    private async Task ConfirmarNoLedgerAsync(OperacaoAssinadaV1 operacao, CancellationToken cancellationToken)
    {
        try
        {
            var operationId = await ledger.RegistrarOperacaoAssinadaV1Async(
                new OperacaoAssinadaV1Dto(operacao.Envelope), cancellationToken);
            if (!string.Equals(operationId, operacao.OperationId, StringComparison.Ordinal))
                throw new InvalidOperationException("O ledger confirmou um identificador diferente da operação assinada.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new IndisponibilidadeLedgerRecebimentoPericiaException(
                "Não foi possível confirmar o recebimento no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private static bool CampoIgual(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

    private static void Validar(ReceberPericiaCommand command)
    {
        if (command.PeritoId <= 0)
            throw new ValidacaoRecebimentoPericiaException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoRecebimentoPericiaException("Perícia inválida.");
    }

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
}
