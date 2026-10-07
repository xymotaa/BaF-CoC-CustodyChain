using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class RomperLacreUseCase(
    IRompimentoLacreStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce geradorNonce) : IRomperLacre
{
    public async Task<PreparacaoRompimentoLacre> PrepararAsync(
        RomperLacreCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        return new PreparacaoRompimentoLacre(
            CriarOperacao(contexto, dados.Justificativa!, Agora()),
            contexto.DidPerito,
            contexto.RotuloEvidencia,
            contexto.NumeroLacre!);
    }

    public async Task<ResultadoRompimentoLacre> ExecutarAsync(
        ConcluirRompimentoLacreCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(new RomperLacreCommand(command.PeritoId, command.PericiaId, command.Justificativa));
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, contexto, dados.Justificativa!);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);

        await store.PersistirAsync(new RompimentoLacrePendente(
            contexto.PericiaId,
            dados.PeritoId,
            contexto.LacreId!.Value,
            contexto.NumeroLacre!,
            dados.Justificativa!,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            Agora(),
            operacao.Envelope.GetRawText(),
            operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoRompimentoLacre(contexto.RotuloEvidencia, contexto.NumeroLacre!, AncoragemPendente: false);
    }

    private async Task<ContextoRompimentoLacre> ObterContextoAsync(
        RomperLacreCommand command,
        CancellationToken cancellationToken)
    {
        var contexto = await store.ObterContextoAsync(command.PericiaId, command.PeritoId, Agora(), cancellationToken)
            ?? throw new RecursoRompimentoLacreNaoEncontradoException(
                "Perícia não encontrada ou vestígio ainda não recebido.");
        if (!contexto.CredencialValida || string.IsNullOrWhiteSpace(contexto.CredencialId))
            throw new CredencialPermissaoInvalidaException(
                "Credencial de permissão inválida, revogada ou expirada para este vestígio (RN10).");
        if (contexto.LacreId is null || contexto.NumeroLacre is null)
            throw new LacreIntactoNaoEncontradoException("Nenhum lacre intacto encontrado para este vestígio.");
        return contexto;
    }

    private JsonElement CriarOperacao(ContextoRompimentoLacre contexto, string justificativa, DateTime rompidoEm) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = "LACRE_ROMPER",
            payload = new
            {
                credentialId = contexto.CredencialId,
                processoId = contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                periciaId = contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                assetId = contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lacreId = contexto.LacreId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                numeroLacre = contexto.NumeroLacre,
                justificativa
            },
            signerDid = contexto.DidPerito,
            keyId = $"{contexto.DidPerito}#key-1",
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            timestamp = rompidoEm.ToString("O"),
            expiresAt = rompidoEm.AddMinutes(5).ToString("O"),
            nonce = Convert.ToBase64String(geradorNonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoRompimentoLacreException("Informe a operação de rompimento assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoRompimentoLacreException(exception.Message);
        }
    }

    private static void ValidarOperacao(
        OperacaoAssinadaV1 operacao,
        ContextoRompimentoLacre contexto,
        string justificativa)
    {
        var envelope = operacao.Envelope;
        var payload = envelope.GetProperty("payload");
        if (envelope.GetProperty("operation").GetString() != "LACRE_ROMPER"
            || envelope.GetProperty("signerDid").GetString() != contexto.DidPerito
            || envelope.GetProperty("keyId").GetString() != $"{contexto.DidPerito}#key-1"
            || !CampoIgual(payload, "credentialId", contexto.CredencialId!)
            || !CampoIgual(payload, "processoId", contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "periciaId", contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "assetId", contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "lacreId", contexto.LacreId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "numeroLacre", contexto.NumeroLacre!)
            || !CampoIgual(payload, "justificativa", justificativa))
            throw new ValidacaoRompimentoLacreException(
                "A operação assinada não corresponde ao lacre, à perícia ou à permissão vigente.");
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
            throw new IndisponibilidadeLedgerRompimentoLacreException(
                "Não foi possível confirmar o rompimento no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private static bool CampoIgual(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

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

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
}
