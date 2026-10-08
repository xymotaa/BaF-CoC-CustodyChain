using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.DestinacaoFinal;

public sealed class AprovarDestinacaoUseCase(
    IDestinacaoFinalStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce nonce) : IAprovarDestinacao
{
    public async Task<PreparacaoAprovacaoDestinacao> PrepararAsync(
        PrepararAprovacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default)
    {
        Validar(command.AprovadorId, command.DescarteId);
        var contexto = await ObterContextoAsync(command.DescarteId, command.AprovadorId, cancellationToken);
        return new PreparacaoAprovacaoDestinacao(CriarOperacao(contexto), contexto.DidAprovador);
    }

    public async Task<ResultadoAprovacaoDestinacao> ExecutarAsync(
        ConcluirAprovacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default)
    {
        Validar(command.AprovadorId, command.DescarteId);
        var contexto = await ObterContextoAsync(command.DescarteId, command.AprovadorId, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        var aprovacao = ValidarOperacao(operacao, contexto, command.AprovadorId);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);
        await store.PersistirAprovacaoAsync(aprovacao, cancellationToken);
        return new ResultadoAprovacaoDestinacao(contexto.Tipo, contexto.RotuloEvidencia, AncoragemPendente: false);
    }

    private async Task<ContextoAprovacaoDestinacao> ObterContextoAsync(long descarteId, long aprovadorId, CancellationToken cancellationToken) =>
        await store.ObterContextoAprovacaoAsync(descarteId, aprovadorId, cancellationToken)
        ?? throw new RecursoDestinacaoFinalNaoEncontradoException(
            "Destinação não encontrada, sem solicitação ancorada ou sem administrador distinto do solicitante (RN16).");

    private JsonElement CriarOperacao(ContextoAprovacaoDestinacao contexto)
    {
        var agora = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation", version = 1, operationId = $"urn:uuid:{Guid.NewGuid()}", operation = "DESTINACAO_APROVAR",
            payload = new
            {
                destinacaoOperationId = contexto.SolicitacaoOperationId,
                assetRef = contexto.AssetRef,
                assetId = contexto.VestigioId.ToString(),
                processoId = contexto.ProcessoId.ToString(),
                tipo = contexto.Tipo,
                autorizacaoCid = contexto.CidAutorizacao,
                autorizacaoHashSha256 = contexto.HashAutorizacao
            },
            signerDid = contexto.DidAprovador, keyId = $"{contexto.DidAprovador}#auth-1", algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1", audience = "custodychain-ledger", timestamp = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"), nonce = Convert.ToBase64String(nonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoDestinacaoFinalException("Informe a aprovação de destinação assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception) { throw new ValidacaoDestinacaoFinalException(exception.Message); }
    }

    private static AprovacaoDestinacaoPendente ValidarOperacao(
        OperacaoAssinadaV1 operacao,
        ContextoAprovacaoDestinacao contexto,
        long aprovadorId)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        var corresponde = Campo(operacao.Envelope, "operation", "DESTINACAO_APROVAR")
            && Campo(operacao.Envelope, "signerDid", contexto.DidAprovador)
            && Campo(payload, "destinacaoOperationId", contexto.SolicitacaoOperationId)
            && Campo(payload, "assetRef", contexto.AssetRef)
            && Campo(payload, "assetId", contexto.VestigioId.ToString())
            && Campo(payload, "processoId", contexto.ProcessoId.ToString())
            && Campo(payload, "tipo", contexto.Tipo)
            && Campo(payload, "autorizacaoCid", contexto.CidAutorizacao)
            && Campo(payload, "autorizacaoHashSha256", contexto.HashAutorizacao);
        if (!corresponde)
            throw new ValidacaoDestinacaoFinalException("A operação assinada não corresponde à solicitação pendente.");

        return new AprovacaoDestinacaoPendente(
            contexto.DescarteId,
            aprovadorId,
            contexto.VestigioId,
            contexto.Tipo,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            operacao.OperationId,
            operacao.Envelope.GetRawText(),
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidAprovador,
            contexto.SolicitacaoOperationId);
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
            throw new IndisponibilidadeLedgerDestinacaoFinalException(
                "Não foi possível confirmar a aprovação no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private static void Validar(long aprovadorId, long descarteId)
    {
        if (aprovadorId <= 0)
            throw new AtorDestinacaoFinalNaoAutorizadoException("A identidade autenticada é inválida.");
        if (descarteId <= 0)
            throw new ValidacaoDestinacaoFinalException("Destinação inválida.", nameof(descarteId));
    }

    private static bool Campo(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && valor.GetString() == esperado;
}
