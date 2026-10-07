using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class RegistrarConsumoOuExaurimentoUseCase(
    IConsumoOuExaurimentoStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce geradorNonce) : IRegistrarConsumoOuExaurimento
{
    private static readonly IReadOnlySet<string> TiposPermitidos = new HashSet<string>(StringComparer.Ordinal)
    {
        "CONSUMO",
        "EXAURIMENTO",
    };

    public async Task<PreparacaoConsumoOuExaurimento> PrepararAsync(
        RegistrarConsumoOuExaurimentoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        return new PreparacaoConsumoOuExaurimento(
            CriarOperacao(contexto, dados, Agora()), contexto.DidPerito, contexto.RotuloEvidencia, dados.Tipo!);
    }

    public async Task<ResultadoConsumoOuExaurimento> ExecutarAsync(
        ConcluirConsumoOuExaurimentoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(new RegistrarConsumoOuExaurimentoCommand(
            command.PeritoId, command.PericiaId, command.Tipo, command.QuantidadeDescrita, command.Justificativa));
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, contexto, dados);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);

        await store.PersistirAsync(new ConsumoOuExaurimentoPendente(
            contexto.PericiaId,
            dados.PeritoId,
            contexto.VestigioId,
            dados.Tipo!,
            dados.QuantidadeDescrita,
            dados.Justificativa!,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            Agora(),
            operacao.Envelope.GetRawText(),
            operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoConsumoOuExaurimento(dados.Tipo!, contexto.RotuloEvidencia, AncoragemPendente: false);
    }

    private async Task<ContextoConsumoOuExaurimento> ObterContextoAsync(
        RegistrarConsumoOuExaurimentoCommand command,
        CancellationToken cancellationToken) =>
        await store.ObterContextoAsync(command.PericiaId, command.PeritoId, Agora(), cancellationToken)
            ?? throw new RecursoConsumoOuExaurimentoNaoEncontradoException(
                "Perícia não encontrada, sem permissão vigente ou lacre ainda não rompido.");

    private JsonElement CriarOperacao(
        ContextoConsumoOuExaurimento contexto,
        RegistrarConsumoOuExaurimentoCommand dados,
        DateTime executadoEm) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = dados.Tipo == "CONSUMO" ? "AMOSTRA_CONSUMIR" : "AMOSTRA_EXAURIR",
            payload = new
            {
                credentialId = contexto.CredencialId,
                processoId = contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                periciaId = contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                assetId = contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
            throw new ValidacaoConsumoOuExaurimentoException("Informe a operação assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoConsumoOuExaurimentoException(exception.Message);
        }
    }

    private static void ValidarOperacao(
        OperacaoAssinadaV1 operacao,
        ContextoConsumoOuExaurimento contexto,
        RegistrarConsumoOuExaurimentoCommand dados)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        var nomeOperacao = dados.Tipo == "CONSUMO" ? "AMOSTRA_CONSUMIR" : "AMOSTRA_EXAURIR";
        if (operacao.Envelope.GetProperty("operation").GetString() != nomeOperacao
            || operacao.Envelope.GetProperty("signerDid").GetString() != contexto.DidPerito
            || operacao.Envelope.GetProperty("keyId").GetString() != $"{contexto.DidPerito}#key-1"
            || !CampoIgual(payload, "credentialId", contexto.CredencialId)
            || !CampoIgual(payload, "processoId", contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "periciaId", contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "assetId", contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgualNulo(payload, "quantidadeDescrita", dados.QuantidadeDescrita)
            || !CampoIgual(payload, "justificativa", dados.Justificativa!))
            throw new ValidacaoConsumoOuExaurimentoException(
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
            throw new IndisponibilidadeLedgerConsumoOuExaurimentoException(
                "Não foi possível confirmar a operação no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private static bool CampoIgual(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

    private static bool CampoIgualNulo(JsonElement objeto, string nome, string? esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && (esperado is null ? valor.ValueKind == JsonValueKind.Null : CampoIgual(objeto, nome, esperado));

    private static RegistrarConsumoOuExaurimentoCommand Normalizar(RegistrarConsumoOuExaurimentoCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorConsumoOuExaurimentoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoConsumoOuExaurimentoException("Perícia inválida.", nameof(command.PericiaId));
        if (string.IsNullOrWhiteSpace(command.Tipo) || !TiposPermitidos.Contains(command.Tipo))
            throw new ValidacaoConsumoOuExaurimentoException("Informe consumo ou exaurimento.", nameof(command.Tipo));
        if (string.IsNullOrWhiteSpace(command.Justificativa))
            throw new ValidacaoConsumoOuExaurimentoException("Informe a justificativa.", nameof(command.Justificativa));

        return command with
        {
            QuantidadeDescrita = Limpar(command.QuantidadeDescrita),
            Justificativa = command.Justificativa.Trim(),
        };
    }

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
