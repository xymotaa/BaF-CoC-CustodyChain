using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.Recebimento;

public sealed class RecusarRecebimentoUseCase(
    IRecebimentoStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce nonce) : IRecusarRecebimento
{
    public async Task<PreparacaoRecebimento> PrepararAsync(RecusarRecebimentoCommand command, CancellationToken cancellationToken = default)
    {
        var recusa = Normalizar(command);
        var contexto = await ObterContextoAsync(recusa, cancellationToken);
        return new PreparacaoRecebimento(CriarOperacao(recusa, contexto), contexto.DidDestino);
    }

    public async Task<ResultadoRecusarRecebimento> ExecutarAsync(ConcluirRecusarRecebimentoCommand command, CancellationToken cancellationToken = default)
    {
        var recusa = Normalizar(command.Recusa);
        var contexto = await ObterContextoAsync(recusa, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, recusa, contexto);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);
        await store.RecusarAsync(CriarRecusaConfirmada(recusa, contexto, operacao), cancellationToken);
        return new ResultadoRecusarRecebimento(contexto.RotuloEvidencia, AncoragemPendente: false);
    }

    private async Task<ContextoRecebimento> ObterContextoAsync(RecusarRecebimentoCommand command, CancellationToken cancellationToken) =>
        await store.ObterContextoAsync(command.MovimentacaoId, command.DestinoId, cancellationToken)
        ?? throw new RecursoRecebimentoNaoEncontradoException("Recebimento pendente, VC de custódia ou remessa assinada não está disponível.");

    private JsonElement CriarOperacao(RecusarRecebimentoCommand recusa, ContextoRecebimento contexto)
    {
        var agora = Agora();
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation", version = 1, operationId = $"urn:uuid:{Guid.NewGuid()}", operation = "REMESSA_RECUSAR",
            payload = new
            {
                credentialId = contexto.CredencialId, remessaOperationId = contexto.RemessaOperationId, assetRef = contexto.AssetRef,
                assetId = contexto.VestigioId.ToString(), processoId = contexto.ProcessoId.ToString(), origemDid = contexto.DidOrigem,
                destinoDid = contexto.DidDestino, motivoRecusa = recusa.MotivoRecusa
            },
            signerDid = contexto.DidDestino, keyId = $"{contexto.DidDestino}#key-1", algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1", audience = "custodychain-ledger", timestamp = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"), nonce = Convert.ToBase64String(nonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoRecebimentoException("Informe a operação de recusa assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception) { throw new ValidacaoRecebimentoException(exception.Message); }
    }

    private static void ValidarOperacao(OperacaoAssinadaV1 operacao, RecusarRecebimentoCommand recusa, ContextoRecebimento contexto)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        var corresponde = Campo(operacao.Envelope, "operation", "REMESSA_RECUSAR") && Campo(operacao.Envelope, "signerDid", contexto.DidDestino)
            && Campo(payload, "credentialId", contexto.CredencialId) && Campo(payload, "remessaOperationId", contexto.RemessaOperationId)
            && Campo(payload, "assetRef", contexto.AssetRef) && Campo(payload, "assetId", contexto.VestigioId.ToString())
            && Campo(payload, "processoId", contexto.ProcessoId.ToString()) && Campo(payload, "origemDid", contexto.DidOrigem)
            && Campo(payload, "destinoDid", contexto.DidDestino) && Campo(payload, "motivoRecusa", recusa.MotivoRecusa!);
        if (!corresponde)
            throw new ValidacaoRecebimentoException("A operação assinada não corresponde à recusa ou à permissão vigente.");
    }

    private async Task ConfirmarNoLedgerAsync(OperacaoAssinadaV1 operacao, CancellationToken cancellationToken)
    {
        try
        {
            var operationId = await ledger.RegistrarOperacaoAssinadaV1Async(new OperacaoAssinadaV1Dto(operacao.Envelope), cancellationToken);
            if (!string.Equals(operationId, operacao.OperationId, StringComparison.Ordinal))
                throw new InvalidOperationException("O ledger confirmou uma operação diferente da solicitada.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new IndisponibilidadeLedgerRecebimentoException("Não foi possível confirmar a recusa no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private RecebimentoRecusado CriarRecusaConfirmada(RecusarRecebimentoCommand recusa, ContextoRecebimento contexto, OperacaoAssinadaV1 operacao) =>
        new(contexto.MovimentacaoId, recusa.DestinoId, recusa.MotivoRecusa!, contexto.EstadoAposRecusa,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime, operacao.Envelope.GetRawText(),
            operacao.OperationId, operacao.CalcularHashCanonicoSemAssinatura(), contexto.DidDestino);

    private static RecusarRecebimentoCommand Normalizar(RecusarRecebimentoCommand command)
    {
        if (command.DestinoId <= 0) throw new AtorRecebimentoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.MovimentacaoId <= 0) throw new ValidacaoRecebimentoException("Recebimento inválido.", nameof(command.MovimentacaoId));
        if (string.IsNullOrWhiteSpace(command.MotivoRecusa)) throw new ValidacaoRecebimentoException("Informe o motivo da recusa.", nameof(command.MotivoRecusa));
        return command with { MotivoRecusa = command.MotivoRecusa.Trim() };
    }

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static bool Campo(JsonElement objeto, string nome, string esperado) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && valor.GetString() == esperado;
}
