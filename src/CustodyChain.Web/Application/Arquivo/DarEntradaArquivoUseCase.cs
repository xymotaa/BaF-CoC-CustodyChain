using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.Arquivo;

public sealed class DarEntradaArquivoUseCase(
    IEntradaArquivoStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce nonce) : IDarEntradaArquivo
{
    public async Task<PreparacaoEntradaArquivo> PrepararAsync(DarEntradaArquivoCommand command, CancellationToken cancellationToken = default)
    {
        var entrada = Normalizar(command);
        var contexto = await ObterContextoAsync(entrada, cancellationToken);
        return new PreparacaoEntradaArquivo(CriarOperacao(entrada, contexto), contexto.DidRecebedor);
    }

    public async Task<ResultadoDarEntradaArquivo> ExecutarAsync(ConcluirEntradaArquivoCommand command, CancellationToken cancellationToken = default)
    {
        var entrada = Normalizar(command.Entrada);
        var contexto = await ObterContextoAsync(entrada, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, entrada, contexto);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);
        await store.PersistirAsync(CriarEntradaConfirmada(entrada, contexto, operacao), cancellationToken);
        return new ResultadoDarEntradaArquivo(contexto.RotuloEvidencia, AncoragemPendente: false);
    }

    private async Task<ContextoEntradaArquivo> ObterContextoAsync(DarEntradaArquivoCommand command, CancellationToken cancellationToken) =>
        await store.ObterContextoAsync(command.VestigioId, command.RecebedorId, cancellationToken)
        ?? throw new RecursoEntradaArquivoNaoEncontradoException("Vestígio, recebimento assinado ou permissão de custódia não está disponível para guarda.");

    private JsonElement CriarOperacao(DarEntradaArquivoCommand entrada, ContextoEntradaArquivo contexto)
    {
        var agora = Agora();
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation", version = 1, operationId = $"urn:uuid:{Guid.NewGuid()}", operation = "GUARDA_REGISTRAR",
            payload = new
            {
                credentialId = contexto.CredencialId, recebimentoOperationId = contexto.RecebimentoOperationId,
                assetRef = contexto.AssetRef, assetId = contexto.VestigioId.ToString(), processoId = contexto.ProcessoId.ToString(),
                central = entrada.Central, posicao = entrada.Posicao, prazoGuardaAte = entrada.PrazoGuardaAte?.ToString("yyyy-MM-dd")
            },
            signerDid = contexto.DidRecebedor, keyId = $"{contexto.DidRecebedor}#key-1", algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1", audience = "custodychain-ledger", timestamp = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"), nonce = Convert.ToBase64String(nonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoEntradaArquivoException("Informe a operação de guarda assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception) { throw new ValidacaoEntradaArquivoException(exception.Message); }
    }

    private static void ValidarOperacao(OperacaoAssinadaV1 operacao, DarEntradaArquivoCommand entrada, ContextoEntradaArquivo contexto)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        var corresponde = Campo(operacao.Envelope, "operation", "GUARDA_REGISTRAR") && Campo(operacao.Envelope, "signerDid", contexto.DidRecebedor)
            && Campo(payload, "credentialId", contexto.CredencialId) && Campo(payload, "recebimentoOperationId", contexto.RecebimentoOperationId)
            && Campo(payload, "assetRef", contexto.AssetRef) && Campo(payload, "assetId", contexto.VestigioId.ToString())
            && Campo(payload, "processoId", contexto.ProcessoId.ToString()) && Campo(payload, "central", entrada.Central!)
            && CampoOuNulo(payload, "posicao", entrada.Posicao) && CampoOuNulo(payload, "prazoGuardaAte", entrada.PrazoGuardaAte?.ToString("yyyy-MM-dd"));
        if (!corresponde)
            throw new ValidacaoEntradaArquivoException("A operação assinada não corresponde à guarda ou à permissão vigente.");
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
            throw new IndisponibilidadeLedgerEntradaArquivoException("Não foi possível confirmar a guarda no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private EntradaArquivoConfirmada CriarEntradaConfirmada(DarEntradaArquivoCommand entrada, ContextoEntradaArquivo contexto, OperacaoAssinadaV1 operacao) =>
        new(contexto.VestigioId, entrada.RecebedorId, entrada.Central!, entrada.Posicao, entrada.PrazoGuardaAte,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime, contexto.RecebimentoOperationId, operacao.Envelope.GetRawText(),
            operacao.OperationId, operacao.CalcularHashCanonicoSemAssinatura(), contexto.DidRecebedor);

    private static DarEntradaArquivoCommand Normalizar(DarEntradaArquivoCommand command)
    {
        if (command.RecebedorId <= 0) throw new AtorEntradaArquivoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.VestigioId <= 0) throw new ValidacaoEntradaArquivoException("Selecione o vestígio.", nameof(command.VestigioId));
        if (string.IsNullOrWhiteSpace(command.Central)) throw new ValidacaoEntradaArquivoException("Informe a central de custódia.", nameof(command.Central));
        return command with { Central = command.Central.Trim(), Posicao = Limpar(command.Posicao) };
    }

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    private static bool Campo(JsonElement objeto, string nome, string esperado) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && valor.GetString() == esperado;
    private static bool CampoOuNulo(JsonElement objeto, string nome, string? esperado) => objeto.TryGetProperty(nome, out var valor) && (esperado is null ? valor.ValueKind == JsonValueKind.Null : Campo(objeto, nome, esperado));
}
