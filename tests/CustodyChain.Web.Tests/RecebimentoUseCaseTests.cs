using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Autenticacao;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.Recebimento;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Tests;

public sealed class RecebimentoUseCaseTests
{
    [Fact]
    public async Task ConfirmarAsync_ComProvaConfirmada_PersisteRecebimentoAncorado()
    {
        var store = new RecebimentoStoreFake();
        var ledger = new LedgerCaptura();
        var useCase = CriarConfirmacao(store, ledger);
        var command = new ConfirmarRecebimentoCommand(2, 15, " LACRE-001 ");
        var preparacao = await useCase.PrepararAsync(command);

        var resultado = await useCase.ExecutarAsync(new ConcluirConfirmarRecebimentoCommand(command, Assinar(preparacao.Operacao)));

        Assert.True(resultado.LacreConfere);
        Assert.False(resultado.AncoragemPendente);
        Assert.Equal(preparacao.Operacao.GetProperty("operationId").GetString(), store.Confirmado!.OperacaoAssinadaId);
        Assert.Equal(store.Confirmado.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task RecusarAsync_ComProvaConfirmada_PreservaEstadoDeRetorno()
    {
        var store = new RecebimentoStoreFake { EstadoRetorno = EstadoRetornoRecusa.Recebido };
        var ledger = new LedgerCaptura();
        var useCase = CriarRecusa(store, ledger);
        var command = new RecusarRecebimentoCommand(2, 15, " Embalagem danificada ");
        var preparacao = await useCase.PrepararAsync(command);

        var resultado = await useCase.ExecutarAsync(new ConcluirRecusarRecebimentoCommand(command, Assinar(preparacao.Operacao)));

        Assert.Equal("RE-001", resultado.RotuloEvidencia);
        Assert.Equal(EstadoRetornoRecusa.Recebido, store.Recusado!.EstadoAposRecusa);
        Assert.Equal("Embalagem danificada", store.Recusado.MotivoRecusa);
        Assert.Equal(store.Recusado.OperacaoAssinadaId, ledger.OperacaoIdRecebida);
    }

    [Fact]
    public async Task ConfirmarAsync_LedgerIndisponivel_NaoPersiste()
    {
        var store = new RecebimentoStoreFake();
        var useCase = CriarConfirmacao(store, new LedgerCaptura { Falhar = true });
        var command = new ConfirmarRecebimentoCommand(2, 15, "LACRE-001");
        var preparacao = await useCase.PrepararAsync(command);

        await Assert.ThrowsAsync<IndisponibilidadeLedgerRecebimentoException>(() =>
            useCase.ExecutarAsync(new ConcluirConfirmarRecebimentoCommand(command, Assinar(preparacao.Operacao))));

        Assert.Null(store.Confirmado);
    }

    [Fact]
    public async Task RecusarAsync_SemMotivo_NaoPreparaOperacao()
    {
        var useCase = CriarRecusa(new RecebimentoStoreFake(), new LedgerCaptura());

        await Assert.ThrowsAsync<ValidacaoRecebimentoException>(() =>
            useCase.PrepararAsync(new RecusarRecebimentoCommand(2, 15, " ")));
    }

    private static ConfirmarRecebimentoUseCase CriarConfirmacao(RecebimentoStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static RecusarRecebimentoUseCase CriarRecusa(RecebimentoStoreFake store, IServicoLedger ledger) =>
        new(store, ledger, new ClockFixo(), new NonceFixo());

    private static JsonElement Assinar(JsonElement operacao)
    {
        var node = JsonNode.Parse(operacao.GetRawText())!.AsObject();
        node["signature"] = new string('A', 86);
        return JsonSerializer.SerializeToElement(node);
    }

    private sealed class RecebimentoStoreFake : IRecebimentoStore
    {
        public EstadoRetornoRecusa EstadoRetorno { get; init; } = EstadoRetornoRecusa.Coletado;
        public RecebimentoConfirmado? Confirmado { get; private set; }
        public RecebimentoRecusado? Recusado { get; private set; }

        public Task<ContextoRecebimento?> ObterContextoAsync(long movimentacaoId, long destinoId, CancellationToken cancellationToken) =>
            Task.FromResult<ContextoRecebimento?>(new(
                movimentacaoId, 42, 10, "urn:uuid:aaaaaaaa-1111-1111-1111-111111111111", "RE-001",
                "did:legal:custodian:origem-001", "did:legal:custodian:teste-001",
                "urn:uuid:cccccccc-1111-1111-1111-111111111111", "urn:uuid:bbbbbbbb-1111-1111-1111-111111111111",
                EstadoRetorno, "LACRE-001"));

        public Task ConfirmarAsync(RecebimentoConfirmado recebimento, CancellationToken cancellationToken)
        {
            Confirmado = recebimento;
            return Task.CompletedTask;
        }

        public Task RecusarAsync(RecebimentoRecusado recebimento, CancellationToken cancellationToken)
        {
            Recusado = recebimento;
            return Task.CompletedTask;
        }
    }

    private sealed class LedgerCaptura : IServicoLedger
    {
        public bool Falhar { get; init; }
        public string? OperacaoIdRecebida { get; private set; }
        public Task<string> RegistrarOperacaoAssinadaV1Async(OperacaoAssinadaV1Dto dto, CancellationToken token = default)
        {
            if (Falhar) throw new InvalidOperationException("Ledger indisponível.");
            OperacaoIdRecebida = dto.Operation.GetProperty("operationId").GetString();
            return Task.FromResult(OperacaoIdRecebida!);
        }
        public Task RegistrarDidV2PendenteAsync(RegistroDidPendenteDto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task AtivarDidV2Async(string did, AtivacaoDidV2Dto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task<string> GerarDidAsync(TipoAtor tipo) => throw new NotSupportedException();
        public Task AtivarDidAsync(string did, string emissor, string senha) => throw new NotSupportedException();
        public Task<DidDocument> ResolverDidAsync(string did) => throw new NotSupportedException();
        public Task<string> EmitirCredencialPermissaoV2Async(CredencialPermissaoV2Dto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task RevogarCredencialV2Async(string credencialId, RevogacaoCredencialV2Dto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task<string> EmitirCredencialCoCAsync(CredencialCoCDto dto, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ResultadoVerificacao> VerificarCredencialAsync(string credential) => throw new NotSupportedException();
        public Task<IReadOnlyList<EstadoRegistro>> HistoricoRegistroAsync(string assetId) => throw new NotSupportedException();
        public Task<CredencialCoCRegistrada> ObterCredencialCoCAsync(string credentialId) => throw new NotSupportedException();
    }

    private sealed class ClockFixo : IClock { public DateTime UtcNow { get; } = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class NonceFixo : IGeradorNonce { public byte[] Gerar(int tamanho) => Enumerable.Range(0, tamanho).Select(i => (byte)i).ToArray(); }
}
