using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.Recebimento;

public sealed class ConfirmarRecebimentoUseCase(
    IRecebimentoStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce nonce) : IConfirmarRecebimento
{
    public async Task<PreparacaoRecebimento> PrepararAsync(ConfirmarRecebimentoCommand command, CancellationToken cancellationToken = default)
    {
        var recebimento = Normalizar(command);
        var contexto = await ObterContextoAsync(recebimento, cancellationToken);
        return new PreparacaoRecebimento(CriarOperacao(recebimento, contexto), contexto.DidDestino);
    }

    public async Task<ResultadoConfirmarRecebimento> ExecutarAsync(ConcluirConfirmarRecebimentoCommand command, CancellationToken cancellationToken = default)
    {
        var recebimento = Normalizar(command.Recebimento);
        var contexto = await ObterContextoAsync(recebimento, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, recebimento, contexto);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);
        await store.ConfirmarAsync(CriarRecebimentoConfirmado(recebimento, contexto, operacao), cancellationToken);

        var lacreConfere = contexto.NumeroLacreEsperado is not null && contexto.NumeroLacreEsperado == recebimento.NumeroLacreConferido;
        return new ResultadoConfirmarRecebimento(contexto.RotuloEvidencia, lacreConfere, AncoragemPendente: false);
    }

    private async Task<ContextoRecebimento> ObterContextoAsync(ConfirmarRecebimentoCommand command, CancellationToken cancellationToken) =>
        await store.ObterContextoAsync(command.MovimentacaoId, command.DestinoId, cancellationToken)
        ?? throw new RecursoRecebimentoNaoEncontradoException("Recebimento pendente, VC de custódia ou remessa assinada não está disponível.");

    private JsonElement CriarOperacao(ConfirmarRecebimentoCommand recebimento, ContextoRecebimento contexto)
    {
        var agora = Agora();
        var lacreConfere = contexto.NumeroLacreEsperado is not null && contexto.NumeroLacreEsperado == recebimento.NumeroLacreConferido;
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation", version = 1, operationId = $"urn:uuid:{Guid.NewGuid()}", operation = "REMESSA_RECEBER",
            payload = new
            {
                credentialId = contexto.CredencialId, remessaOperationId = contexto.RemessaOperationId, assetRef = contexto.AssetRef,
                assetId = contexto.VestigioId.ToString(), processoId = contexto.ProcessoId.ToString(), origemDid = contexto.DidOrigem,
                destinoDid = contexto.DidDestino, numeroLacreEsperado = contexto.NumeroLacreEsperado,
                numeroLacreConferido = recebimento.NumeroLacreConferido, lacreConfere
            },
            signerDid = contexto.DidDestino, keyId = $"{contexto.DidDestino}#key-1", algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1", audience = "custodychain-ledger", timestamp = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"), nonce = Convert.ToBase64String(nonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoRecebimentoException("Informe a operação de recebimento assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception) { throw new ValidacaoRecebimentoException(exception.Message); }
    }

    private static void ValidarOperacao(OperacaoAssinadaV1 operacao, ConfirmarRecebimentoCommand recebimento, ContextoRecebimento contexto)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        var lacreConfere = contexto.NumeroLacreEsperado is not null && contexto.NumeroLacreEsperado == recebimento.NumeroLacreConferido;
        var corresponde = Campo(operacao.Envelope, "operation", "REMESSA_RECEBER") && Campo(operacao.Envelope, "signerDid", contexto.DidDestino)
            && Campo(payload, "credentialId", contexto.CredencialId) && Campo(payload, "remessaOperationId", contexto.RemessaOperationId)
            && Campo(payload, "assetRef", contexto.AssetRef) && Campo(payload, "assetId", contexto.VestigioId.ToString())
            && Campo(payload, "processoId", contexto.ProcessoId.ToString()) && Campo(payload, "origemDid", contexto.DidOrigem)
            && Campo(payload, "destinoDid", contexto.DidDestino) && CampoOuNulo(payload, "numeroLacreEsperado", contexto.NumeroLacreEsperado)
            && Campo(payload, "numeroLacreConferido", recebimento.NumeroLacreConferido!) && Booleano(payload, "lacreConfere", lacreConfere);
        if (!corresponde)
            throw new ValidacaoRecebimentoException("A operação assinada não corresponde ao recebimento ou à permissão vigente.");
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
            throw new IndisponibilidadeLedgerRecebimentoException("Não foi possível confirmar o recebimento no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private RecebimentoConfirmado CriarRecebimentoConfirmado(ConfirmarRecebimentoCommand recebimento, ContextoRecebimento contexto, OperacaoAssinadaV1 operacao) =>
        new(contexto.MovimentacaoId, recebimento.DestinoId, contexto.NumeroLacreEsperado, recebimento.NumeroLacreConferido!,
            contexto.NumeroLacreEsperado is not null && contexto.NumeroLacreEsperado == recebimento.NumeroLacreConferido,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime, operacao.Envelope.GetRawText(),
            operacao.OperationId, operacao.CalcularHashCanonicoSemAssinatura(), contexto.DidDestino);

    private static ConfirmarRecebimentoCommand Normalizar(ConfirmarRecebimentoCommand command)
    {
        if (command.DestinoId <= 0) throw new AtorRecebimentoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.MovimentacaoId <= 0) throw new ValidacaoRecebimentoException("Recebimento inválido.", nameof(command.MovimentacaoId));
        if (string.IsNullOrWhiteSpace(command.NumeroLacreConferido)) throw new ValidacaoRecebimentoException("Informe o número do lacre conferido.", nameof(command.NumeroLacreConferido));
        return command with { NumeroLacreConferido = command.NumeroLacreConferido.Trim() };
    }

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static bool Campo(JsonElement objeto, string nome, string esperado) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && valor.GetString() == esperado;
    private static bool CampoOuNulo(JsonElement objeto, string nome, string? esperado) => objeto.TryGetProperty(nome, out var valor) && (esperado is null ? valor.ValueKind == JsonValueKind.Null : Campo(objeto, nome, esperado));
    private static bool Booleano(JsonElement objeto, string nome, bool esperado) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind is JsonValueKind.True or JsonValueKind.False && valor.GetBoolean() == esperado;
}
