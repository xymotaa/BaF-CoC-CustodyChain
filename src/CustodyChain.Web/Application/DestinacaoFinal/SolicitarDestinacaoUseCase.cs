using System.Security.Cryptography;
using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.DestinacaoFinal;

public sealed class SolicitarDestinacaoUseCase(
    IDestinacaoFinalStore store,
    IArmazenamentoAutorizacao armazenamento,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce nonce) : ISolicitarDestinacao
{
    private static readonly IReadOnlySet<string> TiposPermitidos = new HashSet<string>(StringComparer.Ordinal)
    {
        "DESCARTE",
        "RESTITUICAO",
    };

    public async Task<PreparacaoSolicitacaoDestinacao> PrepararAsync(
        PrepararSolicitacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await ObterContextoAsync(dados.VestigioId!.Value, dados.SolicitanteId, cancellationToken);
        var autorizacao = await armazenamento.ArmazenarAsync(
            dados.ConteudoAutorizacao!, dados.NomeArquivoAutorizacao!, cancellationToken);

        return new PreparacaoSolicitacaoDestinacao(
            CriarOperacao(dados, contexto, autorizacao), contexto.DidSolicitante);
    }

    public async Task<ResultadoSolicitacaoDestinacao> ExecutarAsync(
        ConcluirSolicitacaoDestinacaoCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.SolicitanteId <= 0)
            throw new AtorDestinacaoFinalNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.VestigioId <= 0)
            throw new ValidacaoDestinacaoFinalException("Selecione o vestígio.", nameof(command.VestigioId));

        var contexto = await ObterContextoAsync(command.VestigioId, command.SolicitanteId, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        var solicitacao = ValidarOperacao(operacao, contexto, command.SolicitanteId);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);
        await store.PersistirSolicitacaoAsync(solicitacao, cancellationToken);
        return new ResultadoSolicitacaoDestinacao(contexto.RotuloEvidencia, AncoragemPendente: false);
    }

    private async Task<ContextoSolicitacaoDestinacao> ObterContextoAsync(long vestigioId, long solicitanteId, CancellationToken cancellationToken) =>
        await store.ObterContextoSolicitacaoAsync(vestigioId, solicitanteId, cancellationToken)
        ?? throw new RecursoDestinacaoFinalNaoEncontradoException(
            "Vestígio, guarda confirmada ou permissão de custódia não está disponível para destinação final.");

    private JsonElement CriarOperacao(PrepararSolicitacaoDestinacaoCommand dados, ContextoSolicitacaoDestinacao contexto, AutorizacaoArmazenada autorizacao)
    {
        var agora = Agora();
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation", version = 1, operationId = $"urn:uuid:{Guid.NewGuid()}", operation = "DESTINACAO_SOLICITAR",
            payload = new
            {
                credentialId = contexto.CredencialId, guardaOperationId = contexto.GuardaOperationId,
                assetRef = contexto.AssetRef, assetId = contexto.VestigioId.ToString(), processoId = contexto.ProcessoId.ToString(),
                tipo = dados.Tipo, didMagistrado = dados.DidMagistrado, autorizacaoCid = autorizacao.Cid,
                autorizacaoHashSha256 = CalcularHash(dados.ConteudoAutorizacao!), autorizacaoNomeArquivo = dados.NomeArquivoAutorizacao,
                autorizacaoTamanhoBytes = autorizacao.TamanhoBytes, observacao = Limpar(dados.Observacao)
            },
            signerDid = contexto.DidSolicitante, keyId = $"{contexto.DidSolicitante}#key-1", algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1", audience = "custodychain-ledger", timestamp = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"), nonce = Convert.ToBase64String(nonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacao(JsonElement operacao)
    {
        if (operacao.ValueKind != JsonValueKind.Object)
            throw new ValidacaoDestinacaoFinalException("Informe a solicitação de destinação assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception) { throw new ValidacaoDestinacaoFinalException(exception.Message); }
    }

    private static SolicitacaoDestinacaoPendente ValidarOperacao(OperacaoAssinadaV1 operacao, ContextoSolicitacaoDestinacao contexto, long solicitanteId)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        if (!Campo(operacao.Envelope, "operation", "DESTINACAO_SOLICITAR") || !Campo(operacao.Envelope, "signerDid", contexto.DidSolicitante)
            || !Campo(payload, "credentialId", contexto.CredencialId) || !Campo(payload, "guardaOperationId", contexto.GuardaOperationId)
            || !Campo(payload, "assetRef", contexto.AssetRef) || !Campo(payload, "assetId", contexto.VestigioId.ToString())
            || !Campo(payload, "processoId", contexto.ProcessoId.ToString()) || !CampoDeConjunto(payload, "tipo", TiposPermitidos)
            || !TextoObrigatorio(payload, "didMagistrado") || !TextoObrigatorio(payload, "autorizacaoCid")
            || !Padrao(payload, "autorizacaoHashSha256", "^[a-f0-9]{64}$") || !TextoObrigatorio(payload, "autorizacaoNomeArquivo")
            || !InteiroPositivo(payload, "autorizacaoTamanhoBytes") || !TextoOuNulo(payload, "observacao"))
            throw new ValidacaoDestinacaoFinalException("A operação assinada não corresponde à solicitação ou à permissão vigente.");

        return new SolicitacaoDestinacaoPendente(
            contexto.VestigioId, solicitanteId, payload.GetProperty("tipo").GetString()!, payload.GetProperty("didMagistrado").GetString()!,
            payload.GetProperty("autorizacaoNomeArquivo").GetString()!, payload.GetProperty("autorizacaoCid").GetString()!,
            payload.GetProperty("autorizacaoTamanhoBytes").GetInt64(), payload.GetProperty("autorizacaoHashSha256").GetString()!,
            ObterTextoOuNulo(payload, "observacao"), DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            contexto.CredencialId, contexto.GuardaOperationId,
            operacao.OperationId, operacao.Envelope.GetRawText(), operacao.CalcularHashCanonicoSemAssinatura(), contexto.DidSolicitante);
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
                "Não foi possível confirmar a solicitação no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private static PrepararSolicitacaoDestinacaoCommand Normalizar(PrepararSolicitacaoDestinacaoCommand command)
    {
        if (command.SolicitanteId <= 0) throw new AtorDestinacaoFinalNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.VestigioId is null or <= 0) throw new ValidacaoDestinacaoFinalException("Selecione o vestígio.", nameof(command.VestigioId));
        if (string.IsNullOrWhiteSpace(command.Tipo) || !TiposPermitidos.Contains(command.Tipo)) throw new ValidacaoDestinacaoFinalException("Tipo inválido.", nameof(command.Tipo));
        if (string.IsNullOrWhiteSpace(command.DidMagistrado)) throw new ValidacaoDestinacaoFinalException("Informe o DID do magistrado que autorizou.", nameof(command.DidMagistrado));
        if (string.IsNullOrWhiteSpace(command.NomeArquivoAutorizacao) || command.ConteudoAutorizacao is not { Length: > 0 })
            throw new ValidacaoDestinacaoFinalException("Anexe o mandado judicial.", nameof(command.ConteudoAutorizacao));

        return command with
        {
            Tipo = command.Tipo.Trim(), DidMagistrado = command.DidMagistrado.Trim(),
            NomeArquivoAutorizacao = command.NomeArquivoAutorizacao.Trim(), Observacao = Limpar(command.Observacao)
        };
    }

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static string CalcularHash(byte[] conteudo) => Convert.ToHexStringLower(SHA256.HashData(conteudo));
    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    private static bool Campo(JsonElement objeto, string nome, string esperado) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && valor.GetString() == esperado;
    private static bool CampoDeConjunto(JsonElement objeto, string nome, IReadOnlySet<string> permitidos) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && permitidos.Contains(valor.GetString()!);
    private static bool TextoObrigatorio(JsonElement objeto, string nome) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(valor.GetString());
    private static bool TextoOuNulo(JsonElement objeto, string nome) => objeto.TryGetProperty(nome, out var valor) && (valor.ValueKind == JsonValueKind.Null || valor.ValueKind == JsonValueKind.String);
    private static bool Padrao(JsonElement objeto, string nome, string padrao) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String && System.Text.RegularExpressions.Regex.IsMatch(valor.GetString()!, padrao);
    private static bool InteiroPositivo(JsonElement objeto, string nome) => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.Number && valor.TryGetInt64(out var inteiro) && inteiro > 0;
    private static string? ObterTextoOuNulo(JsonElement objeto, string nome) => objeto.GetProperty(nome).ValueKind == JsonValueKind.Null ? null : objeto.GetProperty(nome).GetString();
}
