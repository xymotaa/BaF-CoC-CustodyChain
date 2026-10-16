using System.Text.Json;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.SignedOperations;

namespace CustodyChain.Web.Application.RevogacaoDid;

public sealed class RevogarDidUseCase(
    IRevogacaoDidStore store,
    IRevogacaoDidLedger ledger,
    IDidRegistry didRegistry,
    IClock clock,
    IGeradorNonce nonce) : IRevogarDid
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AtrasoTentativa = TimeSpan.FromMinutes(2);

    public async Task<PreparacaoRevogacaoDid> PrepararAsync(PrepararRevogacaoDidCommand command, CancellationToken cancellationToken = default)
    {
        var contexto = await ObterContextoElegivelAsync(command.IntervenienteId, command.AdministradorId, command.DidAdministrador, command.Auditoria, cancellationToken);
        var documento = await ResolverDidAsync(contexto.DidAlvo, cancellationToken);
        if (documento is null)
        {
            await RegistrarRejeicaoAsync(command.IntervenienteId, command.AdministradorId, MotivoRejeicaoRevogacaoDid.IdentidadeIndisponivel, command.Auditoria, cancellationToken);
            throw new RevogacaoDidInvalidaException("A identidade não existe no ledger.");
        }
        var governanca = await ObterGovernancaAsync(cancellationToken);
        if (!documento.Ativo || !governanca.AdministradoresAtivos.Contains(command.DidAdministrador, StringComparer.Ordinal))
        {
            await RegistrarRejeicaoAsync(command.IntervenienteId, command.AdministradorId, MotivoRejeicaoRevogacaoDid.IdentidadeIndisponivel, command.Auditoria, cancellationToken);
            throw new RevogacaoDidInvalidaException("A identidade ou a governança no ledger não permite esta revogação.");
        }

        var agora = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        return new PreparacaoRevogacaoDid(JsonSerializer.SerializeToElement(new
        {
            type = "CustodyChainDidRevocation", version = 1, commandId = $"urn:uuid:{Guid.NewGuid()}",
            actorDid = command.DidAdministrador, subjectDid = contexto.DidAlvo,
            expectedDocumentVersion = documento.DocumentVersion, expectedGovernanceRevision = governanca.Revisao,
            reason = "ADMINISTRATIVE_REVOCATION", algorithm = "Ed25519",
            canonicalization = "custodychain-json-c14n-v1", audience = "custodychain-ledger",
            issuedAt = agora.ToString("O"), expiresAt = agora.AddMinutes(5).ToString("O"),
            nonce = Base64Url(nonce.Gerar(24))
        }));
    }

    public async Task<ResultadoRevogacaoDid> ExecutarAsync(ExecutarRevogacaoDidCommand command, CancellationToken cancellationToken = default)
    {
        string commandId;
        try { commandId = LerCommandId(command.Command); }
        catch (RevogacaoDidInvalidaException)
        {
            await RegistrarComandoInvalidoAsync(command, cancellationToken);
            throw;
        }
        string commandCanonico;
        try { commandCanonico = CanonicalizadorJsonCustodyChain.Canonicalizar(command.Command); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await RegistrarComandoInvalidoAsync(command, cancellationToken);
            throw new RevogacaoDidInvalidaException("O comando de revogação não possui formato canônico válido.");
        }
        var existente = await store.ObterPorCommandIdAsync(commandId, cancellationToken);
        if (existente is not null)
        {
            if (!string.Equals(existente.CommandJson, commandCanonico, StringComparison.Ordinal))
            {
                await RegistrarComandoInvalidoAsync(command, cancellationToken);
                throw new RevogacaoDidInvalidaException("commandId já foi usado com conteúdo diferente.");
            }
            if (existente.Estado is EstadoRevogacaoDid.ConfirmadaNoLedger or EstadoRevogacaoDid.FalhaPermanente)
                return new ResultadoRevogacaoDid(existente.NomeAlvo, existente.Estado);
            var assumidaExistente = await store.TentarAssumirAsync(commandId, DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc), Lease, cancellationToken);
            return assumidaExistente is null
                ? new ResultadoRevogacaoDid(existente.NomeAlvo, existente.Estado)
                : await EnviarAoLedgerAsync(assumidaExistente, DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc), reconciliada: false, cancellationToken);
        }

        var contexto = await ObterContextoElegivelAsync(command.IntervenienteId, command.AdministradorId, command.DidAdministrador, command.Auditoria, cancellationToken);
        try { ValidarComando(command.Command, contexto.DidAlvo, command.DidAdministrador); }
        catch (RevogacaoDidInvalidaException)
        {
            await RegistrarComandoInvalidoAsync(command, cancellationToken);
            throw;
        }
        if (string.IsNullOrWhiteSpace(command.KeyId) || string.IsNullOrWhiteSpace(command.Signature))
        {
            await RegistrarComandoInvalidoAsync(command, cancellationToken);
            throw new RevogacaoDidInvalidaException("Informe keyId e assinatura da wallet.");
        }

        var agora = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var saga = await store.CriarOuObterAsync(new NovaSagaRevogacaoDid(commandId, contexto.IntervenienteId,
            command.AdministradorId, contexto.DidAlvo, commandCanonico,
            command.KeyId, command.Signature, command.Auditoria, agora), cancellationToken);

        if (saga.Estado == EstadoRevogacaoDid.ConfirmadaNoLedger || saga.Estado == EstadoRevogacaoDid.FalhaPermanente)
            return new ResultadoRevogacaoDid(saga.NomeAlvo, saga.Estado);

        var assumida = await store.TentarAssumirAsync(commandId, agora, Lease, cancellationToken);
        return assumida is null
            ? new ResultadoRevogacaoDid(saga.NomeAlvo, saga.Estado)
            : await EnviarAoLedgerAsync(assumida, agora, reconciliada: false, cancellationToken);
    }

    private async Task<ResultadoRevogacaoDid> EnviarAoLedgerAsync(SagaRevogacaoDidDto saga, DateTime agora, bool reconciliada, CancellationToken cancellationToken)
    {
        try
        {
            using var documento = JsonDocument.Parse(saga.CommandJson);
            await ledger.RevogarAsync(saga.DidAlvo, new ProvaRevogacaoDid(documento.RootElement.Clone(), saga.KeyId, saga.Signature), cancellationToken);
            await store.ConfirmarAsync(saga.CommandId, agora, reconciliada, cancellationToken);
            return new ResultadoRevogacaoDid(saga.NomeAlvo, EstadoRevogacaoDid.ConfirmadaNoLedger);
        }
        catch (RevogacaoDidRejeitadaException exception)
        {
            return await TratarRejeicaoDoLedgerAsync(saga, exception, agora, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await store.RegistrarFalhaTransitoriaAsync(saga.CommandId, exception.Message, agora, agora.Add(AtrasoTentativa), cancellationToken);
            throw new LedgerRevogacaoDidIndisponivelException("A revogação permanece pendente de confirmação no ledger e será reconciliada automaticamente.", exception);
        }
    }

    private async Task<ContextoRevogacaoDid> ObterContextoElegivelAsync(long intervenienteId, long administradorId, string didAdministrador, ContextoAuditoriaRevogacaoDid auditoria, CancellationToken cancellationToken)
    {
        var contexto = await store.ObterContextoAsync(intervenienteId, cancellationToken)
            ?? new ContextoRevogacaoDid(intervenienteId, administradorId, string.Empty, string.Empty, false, false);
        var motivo = PoliticaRevogacaoDid.Avaliar(contexto.Ativo, didAdministrador, contexto.DidAlvo, contexto.PossuiResponsabilidadesPendentes, true);
        if (motivo == MotivoRejeicaoRevogacaoDid.Nenhum) return contexto;
        await RegistrarRejeicaoAsync(intervenienteId, administradorId, motivo, auditoria, cancellationToken);
        throw new RevogacaoDidInvalidaException("Interveniente não está disponível para revogação.");
    }

    private async Task<GovernancaRevogacaoDid> ObterGovernancaAsync(CancellationToken cancellationToken)
    {
        try { return await ledger.ObterGovernancaAsync(cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { throw new LedgerRevogacaoDidIndisponivelException("Não foi possível consultar a governança no ledger.", exception); }
    }

    private async Task<DocumentoDidAutenticacao?> ResolverDidAsync(string did, CancellationToken cancellationToken)
    {
        try { return await didRegistry.ResolverAsync(did, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { throw new LedgerRevogacaoDidIndisponivelException("Não foi possível consultar a identidade no ledger.", exception); }
    }

    private async Task<ResultadoRevogacaoDid> TratarRejeicaoDoLedgerAsync(
        SagaRevogacaoDidDto saga, RevogacaoDidRejeitadaException exception, DateTime agora, CancellationToken cancellationToken)
    {
        if (exception.Codigo != CodigosRevogacaoDid.ConflitoVersaoTitular)
        {
            if (CodigosRevogacaoDid.EhRejeicaoDefinitiva(exception.Codigo))
            {
                await store.RegistrarFalhaDefinitivaAsync(saga.CommandId, exception.Message, agora, cancellationToken);
                return new ResultadoRevogacaoDid(saga.NomeAlvo, EstadoRevogacaoDid.FalhaPermanente);
            }
            else
            {
                await store.RegistrarFalhaTransitoriaAsync(saga.CommandId, exception.Message, agora, agora.Add(AtrasoTentativa), cancellationToken);
                return new ResultadoRevogacaoDid(saga.NomeAlvo, EstadoRevogacaoDid.FalhaReconciliacao);
            }
        }

        DocumentoDidAutenticacao? documento;
        try { documento = await ResolverDidAsync(saga.DidAlvo, cancellationToken); }
        catch (LedgerRevogacaoDidIndisponivelException)
        {
            await store.RegistrarFalhaTransitoriaAsync(saga.CommandId, exception.Message, agora, agora.Add(AtrasoTentativa), cancellationToken);
            return new ResultadoRevogacaoDid(saga.NomeAlvo, EstadoRevogacaoDid.FalhaReconciliacao);
        }

        switch (PoliticaConflitoVersaoRevogacaoDid.Avaliar(documento))
        {
            case ResolucaoConflitoVersaoRevogacaoDid.ConfirmarRevogacao:
                await store.ConfirmarAsync(saga.CommandId, agora, reconciliada: true, cancellationToken);
                return new ResultadoRevogacaoDid(saga.NomeAlvo, EstadoRevogacaoDid.ConfirmadaNoLedger);
            case ResolucaoConflitoVersaoRevogacaoDid.RejeitarComando:
                await store.RegistrarFalhaDefinitivaAsync(saga.CommandId, exception.Message, agora, cancellationToken);
                return new ResultadoRevogacaoDid(saga.NomeAlvo, EstadoRevogacaoDid.FalhaPermanente);
            default:
                await store.RegistrarFalhaTransitoriaAsync(saga.CommandId, exception.Message, agora, agora.Add(AtrasoTentativa), cancellationToken);
                return new ResultadoRevogacaoDid(saga.NomeAlvo, EstadoRevogacaoDid.FalhaReconciliacao);
        }
    }

    private static void ValidarComando(JsonElement command, string didAlvo, string didAdministrador)
    {
        var valido = command.ValueKind == JsonValueKind.Object
            && command.TryGetProperty("type", out var tipo) && tipo.GetString() == "CustodyChainDidRevocation"
            && command.TryGetProperty("commandId", out var id) && !string.IsNullOrWhiteSpace(id.GetString())
            && command.TryGetProperty("subjectDid", out var alvo) && alvo.GetString() == didAlvo
            && command.TryGetProperty("actorDid", out var ator) && ator.GetString() == didAdministrador
            && command.TryGetProperty("expectedDocumentVersion", out var versao) && versao.ValueKind == JsonValueKind.Number
            && command.TryGetProperty("expectedGovernanceRevision", out var governanca) && governanca.ValueKind == JsonValueKind.Number;
        if (!valido) throw new RevogacaoDidInvalidaException("O comando de revogação não corresponde à identidade disponível.");
    }

    private static string LerCommandId(JsonElement command)
    {
        if (command.ValueKind != JsonValueKind.Object || !command.TryGetProperty("commandId", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            throw new RevogacaoDidInvalidaException("O comando de revogação não contém commandId válido.");
        return id.GetString()!;
    }

    private Task RegistrarComandoInvalidoAsync(ExecutarRevogacaoDidCommand command, CancellationToken cancellationToken) =>
        RegistrarRejeicaoAsync(command.IntervenienteId, command.AdministradorId, MotivoRejeicaoRevogacaoDid.ComandoInvalido, command.Auditoria, cancellationToken);

    private Task RegistrarRejeicaoAsync(long intervenienteId, long administradorId, MotivoRejeicaoRevogacaoDid motivo,
        ContextoAuditoriaRevogacaoDid auditoria, CancellationToken cancellationToken) =>
        store.RegistrarRejeicaoAsync(new RejeicaoRevogacaoDid(intervenienteId, administradorId, motivo, auditoria,
            DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)), cancellationToken);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
