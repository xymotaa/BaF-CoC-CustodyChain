using System.Text.Json;
using System.Security.Cryptography;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.CadastroVestigio;

public sealed class CadastrarVestigioUseCase(
    ICadastroVestigioStore store,
    IArmazenamentoEvidenciaColeta armazenamentoEvidencia,
    IServicoLedger ledger,
    IClock clock,
    IGeradorNonce nonce) : ICadastrarVestigio
{
    public async Task<PreparacaoCadastroVestigio> PrepararAsync(
        CadastrarVestigioCommand command,
        CancellationToken cancellationToken = default)
    {
        var cadastro = Normalizar(command);
        var ator = await ValidarPrecondicoesAsync(cadastro, cancellationToken);
        var integridade = await ArmazenarEvidenciaAsync(cadastro.ArquivoEvidencia, cancellationToken);
        return new PreparacaoCadastroVestigio(CriarOperacao(cadastro, ator, CriarAssetRef(), integridade), ator.Did);
    }

    public async Task<ResultadoCadastroVestigio> ExecutarAsync(
        ConcluirCadastroVestigioCommand command,
        CancellationToken cancellationToken = default)
    {
        var cadastro = Normalizar(command.Cadastro);
        var ator = await ValidarPrecondicoesAsync(cadastro, cancellationToken);
        var operacao = LerOperacaoAssinada(command.OperacaoAssinada);

        var coleta = ValidarVinculoDaOperacao(operacao, cadastro, ator);
        await ConfirmarNoLedgerAsync(operacao, cancellationToken);

        var vestigioId = await store.PersistirAsync(
            CriarCadastroConfirmado(cadastro, operacao, ator, coleta.AssetRef, coleta.Integridade),
            cancellationToken);

        return new ResultadoCadastroVestigio(vestigioId, cadastro.RotuloEvidencia, AncoragemPendente: false);
    }

    private async Task<AtorCadastroVestigio> ValidarPrecondicoesAsync(
        CadastrarVestigioCommand cadastro,
        CancellationToken cancellationToken)
    {
        if (await store.RotuloEvidenciaExisteAsync(cadastro.RotuloEvidencia, cancellationToken))
            throw new ConflitoCadastroVestigioException(
                "Já existe um vestígio com este rótulo de evidência.",
                nameof(cadastro.RotuloEvidencia));

        if (await store.NumeroLacreExisteAsync(cadastro.NumeroLacre, cancellationToken))
            throw new ConflitoCadastroVestigioException(
                "Já existe um lacre com este número.",
                nameof(cadastro.NumeroLacre));

        if (await store.ObterProcessoAtivoAsync(cadastro.ProcessoId, cancellationToken) is null)
            throw new RecursoCadastroVestigioNaoEncontradoException(
                "O processo selecionado não existe ou não está ativo.");

        if (!await store.TipoVestigioExisteAsync(cadastro.TipoVestigioId, cancellationToken))
            throw new RecursoCadastroVestigioNaoEncontradoException(
                "O tipo de vestígio selecionado não existe.");

        return await store.ObterAtorAtivoAsync(
                   cadastro.CriadorId,
                   cadastro.ProcessoId,
                   Agora(),
                   cancellationToken)
               ?? throw new AtorCadastroVestigioNaoAutorizadoException(
                   "O coletor não possui VC vigente para este processo.");
    }

    private JsonElement CriarOperacao(
        CadastrarVestigioCommand cadastro,
        AtorCadastroVestigio ator,
        string assetRef,
        IntegridadeEvidenciaColeta? integridade)
    {
        var agora = Agora();
        return JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainSignedOperation",
            version = 1,
            operationId = $"urn:uuid:{Guid.NewGuid()}",
            operation = "COLETA_REGISTRAR",
            payload = new
            {
                credentialId = ator.CredencialId,
                assetRef,
                processoId = cadastro.ProcessoId.ToString(),
                rotuloEvidencia = cadastro.RotuloEvidencia,
                rotuloConjunto = cadastro.RotuloConjunto,
                numeroEvidencia = cadastro.NumeroEvidencia,
                tipoVestigioId = cadastro.TipoVestigioId.ToString(),
                descricao = cadastro.Descricao,
                localColeta = cadastro.LocalColeta,
                dataHoraColeta = cadastro.DataHoraColeta.ToString("O"),
                metodoColeta = cadastro.MetodoColeta,
                numeroLacre = cadastro.NumeroLacre,
                houveIntercorrencia = cadastro.HouveIntercorrencia,
                descricaoIntercorrencia = cadastro.DescricaoIntercorrencia,
                integrity = integridade is null ? null : new
                {
                    algorithm = integridade.Algorithm,
                    contentHashSha256 = integridade.ContentHashSha256,
                    contentCid = integridade.ContentCid,
                    byteLength = integridade.ByteLength,
                    mediaType = integridade.MediaType,
                    fileName = integridade.FileName
                }
            },
            signerDid = ator.Did,
            keyId = $"{ator.Did}#key-1",
            algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1",
            audience = "custodychain-ledger",
            timestamp = agora.ToString("O"),
            expiresAt = agora.AddMinutes(5).ToString("O"),
            nonce = Convert.ToBase64String(nonce.Gerar(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        });
    }

    private static OperacaoAssinadaV1 LerOperacaoAssinada(JsonElement operacaoAssinada)
    {
        if (operacaoAssinada.ValueKind != JsonValueKind.Object)
            throw new ValidacaoCadastroVestigioException("Informe a operação assinada pela wallet.");

        try { return OperacaoAssinadaV1.Ler(operacaoAssinada.GetRawText()); }
        catch (OperacaoAssinadaInvalidaException exception)
        {
            throw new ValidacaoCadastroVestigioException(exception.Message);
        }
    }

    private static ColetaAssinada ValidarVinculoDaOperacao(
        OperacaoAssinadaV1 operacao, CadastrarVestigioCommand cadastro, AtorCadastroVestigio ator)
    {
        var envelope = operacao.Envelope;
        var payload = envelope.GetProperty("payload");
        var assetRef = ObterAssetRef(payload);
        var corresponde = Texto(envelope, "operation", "COLETA_REGISTRAR")
            && Texto(envelope, "signerDid", ator.Did)
            && Texto(payload, "credentialId", ator.CredencialId)
            && Texto(payload, "processoId", cadastro.ProcessoId.ToString())
            && Texto(payload, "rotuloEvidencia", cadastro.RotuloEvidencia)
            && Texto(payload, "rotuloConjunto", cadastro.RotuloConjunto)
            && TextoOuNulo(payload, "numeroEvidencia", cadastro.NumeroEvidencia)
            && Texto(payload, "tipoVestigioId", cadastro.TipoVestigioId.ToString())
            && Texto(payload, "descricao", cadastro.Descricao)
            && TextoOuNulo(payload, "localColeta", cadastro.LocalColeta)
            && Texto(payload, "dataHoraColeta", cadastro.DataHoraColeta.ToString("O"))
            && TextoOuNulo(payload, "metodoColeta", cadastro.MetodoColeta)
            && Texto(payload, "numeroLacre", cadastro.NumeroLacre)
            && Booleano(payload, "houveIntercorrencia", cadastro.HouveIntercorrencia)
            && TextoOuNulo(payload, "descricaoIntercorrencia", cadastro.DescricaoIntercorrencia);

        if (!corresponde)
            throw new ValidacaoCadastroVestigioException(
                "A operação assinada não corresponde à coleta ou à permissão vigente.");

        return new ColetaAssinada(assetRef, LerIntegridade(payload));
    }

    private async Task ConfirmarNoLedgerAsync(OperacaoAssinadaV1 operacao, CancellationToken cancellationToken)
    {
        try
        {
            var operationId = await ledger.RegistrarOperacaoAssinadaV1Async(
                new OperacaoAssinadaV1Dto(operacao.Envelope), cancellationToken);
            if (!string.Equals(operationId, operacao.OperationId, StringComparison.Ordinal))
                throw new InvalidOperationException("O ledger confirmou uma operação diferente da solicitada.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new IndisponibilidadeLedgerCadastroVestigioException(
                "Não foi possível confirmar a coleta no ledger. Reenvie a mesma prova.", exception);
        }
    }

    private CadastroVestigioPendente CriarCadastroConfirmado(
        CadastrarVestigioCommand cadastro,
        OperacaoAssinadaV1 operacao,
        AtorCadastroVestigio ator,
        string assetRef,
        IntegridadeEvidenciaColeta? integridade) =>
        new(
            assetRef, cadastro.RotuloEvidencia, cadastro.RotuloConjunto, cadastro.NumeroEvidencia,
            cadastro.ProcessoId, cadastro.TipoVestigioId, cadastro.Descricao, cadastro.CriadorId,
            cadastro.LocalColeta ?? string.Empty, cadastro.DataHoraColeta, cadastro.MetodoColeta,
            cadastro.HouveIntercorrencia, cadastro.DescricaoIntercorrencia, cadastro.NumeroLacre,
            DateTimeOffset.Parse(operacao.Envelope.GetProperty("timestamp").GetString()!).UtcDateTime,
            Agora(), operacao.Envelope.GetRawText(), operacao.OperationId,
            operacao.CalcularHashCanonicoSemAssinatura(), ator.Did, integridade);

    private async Task<IntegridadeEvidenciaColeta?> ArmazenarEvidenciaAsync(
        ArquivoEvidenciaColeta? arquivo,
        CancellationToken cancellationToken)
    {
        if (arquivo is null)
            return null;

        var armazenado = await armazenamentoEvidencia.ArmazenarAsync(
            arquivo.Conteudo, arquivo.NomeArquivo, cancellationToken);
        if (armazenado.TamanhoBytes != arquivo.Conteudo.LongLength)
            throw new InvalidOperationException("O armazenamento retornou tamanho diferente do arquivo enviado.");

        return new IntegridadeEvidenciaColeta(
            "SHA-256",
            Convert.ToHexStringLower(SHA256.HashData(arquivo.Conteudo)),
            armazenado.Cid,
            armazenado.TamanhoBytes,
            arquivo.MediaType,
            arquivo.NomeArquivo);
    }

    private static IntegridadeEvidenciaColeta? LerIntegridade(JsonElement payload)
    {
        if (!payload.TryGetProperty("integrity", out var integridade))
            throw new ValidacaoCadastroVestigioException(
                "A operação assinada não informa a atestação de integridade.");

        if (integridade.ValueKind == JsonValueKind.Null)
            return null;

        if (integridade.ValueKind != JsonValueKind.Object
            || !Texto(integridade, "algorithm", "SHA-256")
            || !Padrao(integridade, "contentHashSha256", "^[a-f0-9]{64}$")
            || !TextoObrigatorio(integridade, "contentCid")
            || !InteiroPositivo(integridade, "byteLength")
            || !TextoObrigatorio(integridade, "mediaType")
            || !TextoObrigatorio(integridade, "fileName"))
        {
            throw new ValidacaoCadastroVestigioException(
                "A atestação de integridade assinada é inválida.");
        }

        return new IntegridadeEvidenciaColeta(
            integridade.GetProperty("algorithm").GetString()!,
            integridade.GetProperty("contentHashSha256").GetString()!,
            integridade.GetProperty("contentCid").GetString()!,
            integridade.GetProperty("byteLength").GetInt64(),
            integridade.GetProperty("mediaType").GetString()!,
            integridade.GetProperty("fileName").GetString()!);
    }

    private static bool Texto(JsonElement objeto, string nome, string esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String && valor.GetString() == esperado;

    private static bool TextoOuNulo(JsonElement objeto, string nome, string? esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && (esperado is null ? valor.ValueKind == JsonValueKind.Null : Texto(objeto, nome, esperado));

    private static bool Booleano(JsonElement objeto, string nome, bool esperado) =>
        objeto.TryGetProperty(nome, out var valor)
        && (valor.ValueKind is JsonValueKind.True or JsonValueKind.False)
        && valor.GetBoolean() == esperado;

    private static bool Padrao(JsonElement objeto, string nome, string padrao) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && System.Text.RegularExpressions.Regex.IsMatch(valor.GetString()!, padrao);

    private static bool TextoObrigatorio(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(valor.GetString());

    private static bool InteiroPositivo(JsonElement objeto, string nome) =>
        objeto.TryGetProperty(nome, out var valor)
        && valor.ValueKind == JsonValueKind.Number
        && valor.TryGetInt64(out var inteiro)
        && inteiro > 0;

    private static string ObterAssetRef(JsonElement payload)
    {
        if (!payload.TryGetProperty("assetRef", out var valor)
            || valor.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(valor.GetString()?.Replace("urn:uuid:", string.Empty, StringComparison.Ordinal), "D", out _)
            || !valor.GetString()!.StartsWith("urn:uuid:", StringComparison.Ordinal))
            throw new ValidacaoCadastroVestigioException("A operação assinada não possui um identificador de ativo válido.");

        return valor.GetString()!;
    }

    private static CadastrarVestigioCommand Normalizar(CadastrarVestigioCommand command)
    {
        if (command.ProcessoId <= 0 || command.TipoVestigioId <= 0 || command.CriadorId <= 0)
            throw new ValidacaoCadastroVestigioException("Dados obrigatórios inválidos.");
        if (command.HouveIntercorrencia && string.IsNullOrWhiteSpace(command.DescricaoIntercorrencia))
            throw new ValidacaoCadastroVestigioException(
                "Descreva a intercorrência informada.", nameof(command.DescricaoIntercorrencia));
        if (command.ArquivoEvidencia is { } arquivo
            && (arquivo.Conteudo.Length == 0
                || string.IsNullOrWhiteSpace(arquivo.NomeArquivo)
                || arquivo.NomeArquivo.Length > 255
                || string.IsNullOrWhiteSpace(arquivo.MediaType)
                || arquivo.MediaType.Length > 127))
            throw new ValidacaoCadastroVestigioException(
                "O arquivo de evidência informado é inválido.", nameof(command.ArquivoEvidencia));

        return command with
        {
            RotuloEvidencia = Exigir(command.RotuloEvidencia, nameof(command.RotuloEvidencia)),
            RotuloConjunto = Exigir(command.RotuloConjunto, nameof(command.RotuloConjunto)),
            Descricao = Exigir(command.Descricao, nameof(command.Descricao)),
            NumeroLacre = Exigir(command.NumeroLacre, nameof(command.NumeroLacre)),
            NumeroEvidencia = Limpar(command.NumeroEvidencia),
            LocalColeta = Limpar(command.LocalColeta),
            MetodoColeta = Limpar(command.MetodoColeta),
            DescricaoIntercorrencia = command.HouveIntercorrencia
                ? Exigir(command.DescricaoIntercorrencia, nameof(command.DescricaoIntercorrencia)) : null,
            DataHoraColeta = command.DataHoraColeta.ToUniversalTime()
        };
    }

    private sealed record ColetaAssinada(string AssetRef, IntegridadeEvidenciaColeta? Integridade);

    private DateTime Agora() => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
    private static string CriarAssetRef() => $"urn:uuid:{Guid.NewGuid()}";
    private static string Exigir(string? valor, string campo) =>
        Limpar(valor) ?? throw new ValidacaoCadastroVestigioException("Informe um valor.", campo);
    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
