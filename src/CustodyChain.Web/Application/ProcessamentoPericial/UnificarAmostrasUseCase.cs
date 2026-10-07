using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class UnificarAmostrasUseCase(
    IUnificacaoAmostrasStore store,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce geradorNonce) : IUnificarAmostras
{
    public async Task<PreparacaoUnificacaoAmostras> PrepararAsync(
        UnificarAmostrasCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var (contexto, origens) = await ObterContextoEOrigensAsync(dados, cancellationToken);
        if (await store.RotuloEvidenciaExisteAsync(dados.RotuloEvidenciaResultante!, cancellationToken))
            throw new ConflitoUnificacaoAmostrasException("Já existe um vestígio com este rótulo de evidência.");

        return new PreparacaoUnificacaoAmostras(
            CriarOperacao(contexto, origens, dados, Agora()), contexto.DidPerito, origens.Count);
    }

    public async Task<ResultadoUnificacaoAmostras> ExecutarAsync(
        ConcluirUnificacaoAmostrasCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(new UnificarAmostrasCommand(
            command.PeritoId,
            command.PericiaId,
            command.OutrosVestigiosOrigemIds,
            command.RotuloEvidenciaResultante,
            command.DescricaoResultante,
            command.Justificativa));
        var (contexto, origens) = await ObterContextoEOrigensAsync(dados, cancellationToken);
        var operacao = LerOperacao(command.OperacaoAssinada);
        ValidarOperacao(operacao, contexto, origens, dados);

        if (await store.RotuloEvidenciaExisteAsync(dados.RotuloEvidenciaResultante!, cancellationToken))
            throw new ConflitoUnificacaoAmostrasException("Já existe um vestígio com este rótulo de evidência.");

        await ConfirmarNoLedgerAsync(operacao, cancellationToken);

        await store.PersistirAsync(new UnificacaoAmostrasPendente(
            contexto.PericiaId,
            dados.PeritoId,
            origens,
            dados.RotuloEvidenciaResultante!,
            dados.DescricaoResultante!,
            dados.Justificativa!,
            CalcularHashCombinado(origens),
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            Agora(),
            operacao.Envelope.GetRawText(),
            operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoUnificacaoAmostras(origens.Count, dados.RotuloEvidenciaResultante!, AncoragemPendente: false);
    }

    private async Task<(ContextoUnificacaoAmostras Contexto, IReadOnlyList<OrigemUnificacaoAmostra> Origens)> ObterContextoEOrigensAsync(
        UnificarAmostrasCommand command,
        CancellationToken cancellationToken)
    {
        var agora = Agora();
        var contexto = await store.ObterContextoAsync(command.PericiaId, command.PeritoId, agora, cancellationToken)
            ?? throw new RecursoUnificacaoAmostrasNaoEncontradoException(
                "Perícia não encontrada, sem permissão vigente ou lacre ainda não rompido.");
        var ids = OutrosIds(command).Append(contexto.VestigioPrincipalId).Distinct().Order().ToList();
        var origens = await store.ObterOrigensAsync(ids, command.PeritoId, agora, cancellationToken);
        if (origens.Count != ids.Count || !PossuemMesmoConjuntoEProcesso(origens))
            throw new ValidacaoUnificacaoAmostrasException(
                "Cada origem precisa ter VC vigente do perito e compartilhar o mesmo conjunto e processo (RN18).",
                nameof(command.OutrosVestigiosOrigemIds));

        return (contexto, origens.OrderBy(origem => origem.VestigioId).ToList());
    }

    private JsonElement CriarOperacao(
        ContextoUnificacaoAmostras contexto,
        IReadOnlyList<OrigemUnificacaoAmostra> origens,
        UnificarAmostrasCommand dados,
        DateTime executadoEm) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = "AMOSTRA_UNIFICAR",
            payload = new
            {
                periciaId = contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                origens = origens.Select(origem => new
                {
                    credentialId = origem.CredencialId,
                    processoId = origem.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    assetId = origem.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    hashVestigio = origem.HashSha256
                }),
                rotuloEvidenciaResultante = dados.RotuloEvidenciaResultante,
                descricaoResultante = dados.DescricaoResultante,
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
            throw new ValidacaoUnificacaoAmostrasException("Informe a operação assinada pela wallet.");
        try { return OperacaoAssinadaV1.Ler(operacao.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoUnificacaoAmostrasException(exception.Message);
        }
    }

    private static void ValidarOperacao(
        OperacaoAssinadaV1 operacao,
        ContextoUnificacaoAmostras contexto,
        IReadOnlyList<OrigemUnificacaoAmostra> origens,
        UnificarAmostrasCommand dados)
    {
        var payload = operacao.Envelope.GetProperty("payload");
        if (operacao.Envelope.GetProperty("operation").GetString() != "AMOSTRA_UNIFICAR"
            || operacao.Envelope.GetProperty("signerDid").GetString() != contexto.DidPerito
            || operacao.Envelope.GetProperty("keyId").GetString() != $"{contexto.DidPerito}#key-1"
            || !CampoIgual(payload, "periciaId", contexto.PericiaId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || !OrigensIguais(payload, origens)
            || !CampoIgual(payload, "rotuloEvidenciaResultante", dados.RotuloEvidenciaResultante!)
            || !CampoIgual(payload, "descricaoResultante", dados.DescricaoResultante!)
            || !CampoIgual(payload, "justificativa", dados.Justificativa!))
            throw new ValidacaoUnificacaoAmostrasException(
                "A operação assinada não corresponde às origens ou às permissões vigentes.");
    }

    private static bool OrigensIguais(JsonElement payload, IReadOnlyList<OrigemUnificacaoAmostra> origens)
    {
        if (!payload.TryGetProperty("origens", out var assinadas) || assinadas.ValueKind != JsonValueKind.Array
            || assinadas.GetArrayLength() != origens.Count)
            return false;

        return assinadas.EnumerateArray().Zip(origens).All(par =>
            par.First.ValueKind == JsonValueKind.Object
            && CampoIgual(par.First, "credentialId", par.Second.CredencialId)
            && CampoIgual(par.First, "processoId", par.Second.ProcessoId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            && CampoIgual(par.First, "assetId", par.Second.VestigioId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            && CampoIgualNulo(par.First, "hashVestigio", par.Second.HashSha256));
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
            throw new IndisponibilidadeLedgerUnificacaoAmostrasException(
                "Não foi possível confirmar a unificação no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private static bool PossuemMesmoConjuntoEProcesso(IReadOnlyList<OrigemUnificacaoAmostra> origens) =>
        origens.Select(origem => origem.RotuloConjunto).Distinct(StringComparer.Ordinal).Count() == 1
        && origens.Select(origem => origem.ProcessoId).Distinct().Count() == 1;

    private static bool CampoIgual(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && string.Equals(valor.GetString(), esperado, StringComparison.Ordinal);

    private static bool CampoIgualNulo(JsonElement objeto, string nome, string? esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && (esperado is null ? valor.ValueKind == JsonValueKind.Null : CampoIgual(objeto, nome, esperado));

    private static UnificarAmostrasCommand Normalizar(UnificarAmostrasCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorUnificacaoAmostrasNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoUnificacaoAmostrasException("Perícia inválida.", nameof(command.PericiaId));

        var outrosIds = (command.OutrosVestigiosOrigemIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(valor => long.TryParse(valor, out var id) && id > 0 ? id : (long?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .Order()
            .ToList();
        if (outrosIds.Count == 0)
            throw new ValidacaoUnificacaoAmostrasException("Informe ao menos um outro vestígio de origem.", nameof(command.OutrosVestigiosOrigemIds));
        if (string.IsNullOrWhiteSpace(command.RotuloEvidenciaResultante))
            throw new ValidacaoUnificacaoAmostrasException("Informe o rótulo de evidência resultante.", nameof(command.RotuloEvidenciaResultante));
        if (string.IsNullOrWhiteSpace(command.DescricaoResultante))
            throw new ValidacaoUnificacaoAmostrasException("Informe a descrição resultante.", nameof(command.DescricaoResultante));
        if (string.IsNullOrWhiteSpace(command.Justificativa))
            throw new ValidacaoUnificacaoAmostrasException("Informe a justificativa.", nameof(command.Justificativa));

        return command with
        {
            OutrosVestigiosOrigemIds = string.Join(',', outrosIds),
            RotuloEvidenciaResultante = command.RotuloEvidenciaResultante.Trim(),
            DescricaoResultante = command.DescricaoResultante.Trim(),
            Justificativa = command.Justificativa.Trim(),
        };
    }

    private static List<long> OutrosIds(UnificarAmostrasCommand command) =>
        command.OutrosVestigiosOrigemIds!.Split(',').Select(long.Parse).ToList();

    private static string? CalcularHashCombinado(IEnumerable<OrigemUnificacaoAmostra> origens)
    {
        var hashes = origens.Where(origem => !string.IsNullOrEmpty(origem.HashSha256))
            .OrderBy(origem => origem.RotuloEvidencia, StringComparer.Ordinal)
            .Select(origem => origem.HashSha256!);
        var combinado = string.Concat(hashes);
        return combinado.Length == 0 ? null : CalcularHash(combinado);
    }

    private static string CalcularHash(string valor) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
}
