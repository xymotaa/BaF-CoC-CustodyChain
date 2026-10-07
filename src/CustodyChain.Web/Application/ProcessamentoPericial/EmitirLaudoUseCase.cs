using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class EmitirLaudoUseCase(
    IEmissaoLaudoStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce geradorNonce) : IEmitirLaudo
{
    public async Task<PreparacaoEmissaoLaudo> PrepararAsync(
        EmitirLaudoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        var emitidoEm = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var laudo = CriarDadosLaudo(dados.Conteudo!, contexto, emitidoEm);
        var operacao = CriarOperacao(contexto, laudo, emitidoEm);

        return new PreparacaoEmissaoLaudo(operacao, contexto.DidPerito, contexto.RotuloEvidencia, laudo.Numero);
    }

    public async Task<ResultadoEmissaoLaudo> ExecutarAsync(
        ConcluirEmissaoLaudoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(new EmitirLaudoCommand(command.PeritoId, command.PericiaId, command.Conteudo));
        var contexto = await ObterContextoAsync(dados, cancellationToken);
        var operacao = LerOperacaoAssinada(command.OperacaoAssinada);
        var emitidoEm = DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime;
        var laudo = CriarDadosLaudo(dados.Conteudo!, contexto, emitidoEm);

        ValidarOperacaoDeLaudo(operacao, contexto, laudo);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);
        var confirmadoEm = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        await store.PersistirAsync(new LaudoPendente(
            contexto.PericiaId,
            dados.PeritoId,
            laudo.Numero,
            dados.Conteudo!,
            contexto.HashVestigios!,
            laudo.Hash,
            emitidoEm,
            confirmadoEm,
            operacao.Envelope.GetRawText(),
            operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoEmissaoLaudo(contexto.RotuloEvidencia, laudo.Numero, AncoragemPendente: false);
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
            throw new IndisponibilidadeLedgerEmissaoLaudoException(
                "Não foi possível confirmar a autorização do laudo no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private async Task<ContextoEmissaoLaudo> ObterContextoAsync(
        EmitirLaudoCommand command,
        CancellationToken cancellationToken)
    {
        var contexto = await store.ObterContextoAsync(command.PericiaId, command.PeritoId, cancellationToken)
            ?? throw new RecursoEmissaoLaudoNaoEncontradoException(
                "Perícia não encontrada, sem permissão vigente ou lacre ainda não rompido.");
        if (string.IsNullOrEmpty(contexto.HashVestigios))
            throw new HashVestigioAusenteException(
                "Vestígio sem hash SHA-256 registrado (RN13) — não é possível vincular o laudo.");
        return contexto;
    }

    private JsonElement CriarOperacao(ContextoEmissaoLaudo contexto, DadosLaudo laudo, DateTime emitidoEm)
    {
        var expiracao = emitidoEm.AddMinutes(5);
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = "LAUDO_EMITIR",
            payload = new
            {
                credentialId = contexto.CredencialId,
                processoId = contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                assetId = contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                periciaId = contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                numeroLaudo = laudo.Numero,
                hashLaudo = laudo.Hash,
                hashVestigio = contexto.HashVestigios
            },
            signerDid = contexto.DidPerito,
            keyId = $"{contexto.DidPerito}#key-1",
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            timestamp = emitidoEm.ToString("O"),
            expiresAt = expiracao.ToString("O"),
            nonce = Convert.ToBase64String(geradorNonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacaoAssinada(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoEmissaoLaudoException("Informe a operação de laudo assinada pela wallet.");

        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoEmissaoLaudoException(exception.Message);
        }
    }

    private static void ValidarOperacaoDeLaudo(
        OperacaoAssinadaV1 operacao,
        ContextoEmissaoLaudo contexto,
        DadosLaudo laudo)
    {
        var envelope = operacao.Envelope;
        var payload = envelope.GetProperty("payload");
        if (envelope.GetProperty("operation").GetString() != "LAUDO_EMITIR"
            || envelope.GetProperty("signerDid").GetString() != contexto.DidPerito
            || envelope.GetProperty("keyId").GetString() != $"{contexto.DidPerito}#key-1"
            || !CampoIgual(payload, "credentialId", contexto.CredencialId)
            || !CampoIgual(payload, "processoId", contexto.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "assetId", contexto.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "periciaId", contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !CampoIgual(payload, "numeroLaudo", laudo.Numero)
            || !CampoIgual(payload, "hashLaudo", laudo.Hash)
            || !CampoIgual(payload, "hashVestigio", contexto.HashVestigios!))
            throw new ValidacaoEmissaoLaudoException(
                "A operação assinada não corresponde ao laudo, à perícia ou à permissão vigente.");
    }

    private static bool CampoIgual(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

    private static DadosLaudo CriarDadosLaudo(string conteudo, ContextoEmissaoLaudo contexto, DateTime emitidoEm) =>
        new($"LAUDO-{emitidoEm:yyyy}-{contexto.PericiaId:D6}", CalcularHash(conteudo + contexto.HashVestigios));

    private static EmitirLaudoCommand Normalizar(EmitirLaudoCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorEmissaoLaudoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoEmissaoLaudoException("Perícia inválida.", nameof(command.PericiaId));
        if (string.IsNullOrWhiteSpace(command.Conteudo))
            throw new ValidacaoEmissaoLaudoException("Informe o conteúdo do laudo.", nameof(command.Conteudo));

        return command with { Conteudo = command.Conteudo.Trim() };
    }

    private static string CalcularHash(string conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));

    private sealed record DadosLaudo(string Numero, string Hash);
}
