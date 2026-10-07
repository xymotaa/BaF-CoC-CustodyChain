using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class FracionarAmostraUseCase(
    IFracionamentoAmostraStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce geradorNonce) : IFracionarAmostra
{
    public async Task<PreparacaoFracionamentoAmostra> PrepararAsync(
        FracionarAmostraCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        if (await store.RotuloEvidenciaExisteAsync(dados.RotuloEvidenciaResultante!, cancellationToken))
            throw new ConflitoFracionamentoAmostraException("Já existe um vestígio com este rótulo de evidência.");

        return new PreparacaoFracionamentoAmostra(
            CriarOperacao(contexto, dados, Agora()), contexto.DidPerito, contexto.RotuloEvidenciaOrigem);
    }

    public async Task<ResultadoFracionamentoAmostra> ExecutarAsync(
        ConcluirFracionamentoAmostraCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(new FracionarAmostraCommand(
            command.PeritoId,
            command.PericiaId,
            command.RotuloEvidenciaResultante,
            command.DescricaoResultante,
            command.QuantidadeDescrita,
            command.Justificativa));
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, contexto, dados);

        if (await store.RotuloEvidenciaExisteAsync(dados.RotuloEvidenciaResultante!, cancellationToken))
            throw new ConflitoFracionamentoAmostraException("Já existe um vestígio com este rótulo de evidência.");

        await ConfirmarNoLedgerAsync(operacao, cancellationToken);

        await store.PersistirAsync(new FracionamentoAmostraPendente(
            contexto.PericiaId,
            dados.PeritoId,
            contexto.RotuloEvidenciaOrigem,
            contexto.RotuloConjunto,
            contexto.ProcessoId,
            contexto.TipoVestigioId,
            contexto.HashSha256,
            dados.RotuloEvidenciaResultante!,
            dados.DescricaoResultante!,
            dados.QuantidadeDescrita,
            dados.Justificativa!,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            Agora(),
            operacao.Envelope.GetRawText(),
            operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoFracionamentoAmostra(
            contexto.RotuloEvidenciaOrigem, dados.RotuloEvidenciaResultante!, AncoragemPendente: false);
    }

    private async Task<ContextoFracionamentoAmostra> ObterContextoAsync(
        FracionarAmostraCommand command,
        CancellationToken cancellationToken) =>
        await store.ObterContextoAsync(command.PericiaId, command.PeritoId, Agora(), cancellationToken)
            ?? throw new RecursoFracionamentoAmostraNaoEncontradoException(
                "Perícia não encontrada, sem permissão vigente ou lacre ainda não rompido.");

    private JsonElement CriarOperacao(
        ContextoFracionamentoAmostra contexto,
        FracionarAmostraCommand dados,
        DateTime executadoEm) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = "AMOSTRA_FRACIONAR",
            payload = new
            {
                credentialId = contexto.CredencialId,
                processoId = contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                periciaId = contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                assetId = contexto.VestigioOrigemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                hashVestigio = contexto.HashSha256,
                rotuloEvidenciaResultante = dados.RotuloEvidenciaResultante,
                descricaoResultante = dados.DescricaoResultante,
                quantidadeDescrita = dados.QuantidadeDescrita,
                justificativa = dados.Justificativa
            },
            signerDid = contexto.DidPerito,
            keyId = $"{contexto.DidPerito}#key-1",
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            timestamp = executadoEm.ToString("O"),
            expiresAt = executadoEm.AddMinutes(5).ToString("O"),
            nonce = Convert.ToBase64String(geradorNonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoFracionamentoAmostraException("Informe a operação assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoFracionamentoAmostraException(exception.Message);
        }
    }

    private static void ValidarOperacao(
        OperacaoAssinadaV1 operacao,
        ContextoFracionamentoAmostra contexto,
        FracionarAmostraCommand dados)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        if (operacao.Envelope.GetProperty("operation").GetString() != "AMOSTRA_FRACIONAR"
            || operacao.Envelope.GetProperty("signerDid").GetString() != contexto.DidPerito
            || operacao.Envelope.GetProperty("keyId").GetString() != $"{contexto.DidPerito}#key-1"
            || !CampoIgual(payload, "credentialId", contexto.CredencialId)
            || !CampoIgual(payload, "processoId", contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "periciaId", contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "assetId", contexto.VestigioOrigemId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgualNulo(payload, "hashVestigio", contexto.HashSha256)
            || !CampoIgual(payload, "rotuloEvidenciaResultante", dados.RotuloEvidenciaResultante!)
            || !CampoIgual(payload, "descricaoResultante", dados.DescricaoResultante!)
            || !CampoIgualNulo(payload, "quantidadeDescrita", dados.QuantidadeDescrita)
            || !CampoIgual(payload, "justificativa", dados.Justificativa!))
            throw new ValidacaoFracionamentoAmostraException(
                "A operação assinada não corresponde ao fracionamento ou à permissão vigente.");
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
            throw new IndisponibilidadeLedgerFracionamentoAmostraException(
                "Não foi possível confirmar o fracionamento no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private static bool CampoIgual(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

    private static bool CampoIgualNulo(JsonElement objeto, string nome, string? esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && (esperado is null ? valor.ValueKind == JsonValueKind.Null : CampoIgual(objeto, nome, esperado));

    private static FracionarAmostraCommand Normalizar(FracionarAmostraCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorFracionamentoAmostraNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoFracionamentoAmostraException("Perícia inválida.", nameof(command.PericiaId));
        if (string.IsNullOrWhiteSpace(command.RotuloEvidenciaResultante))
            throw new ValidacaoFracionamentoAmostraException("Informe o rótulo de evidência resultante.", nameof(command.RotuloEvidenciaResultante));
        if (string.IsNullOrWhiteSpace(command.DescricaoResultante))
            throw new ValidacaoFracionamentoAmostraException("Informe a descrição resultante.", nameof(command.DescricaoResultante));
        if (string.IsNullOrWhiteSpace(command.Justificativa))
            throw new ValidacaoFracionamentoAmostraException("Informe a justificativa.", nameof(command.Justificativa));

        return command with
        {
            RotuloEvidenciaResultante = command.RotuloEvidenciaResultante.Trim(),
            DescricaoResultante = command.DescricaoResultante.Trim(),
            QuantidadeDescrita = Limpar(command.QuantidadeDescrita),
            Justificativa = command.Justificativa.Trim(),
        };
    }

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
