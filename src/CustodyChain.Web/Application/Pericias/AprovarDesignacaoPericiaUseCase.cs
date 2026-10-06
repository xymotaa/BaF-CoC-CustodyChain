using System.Text.Json;
using System.Text.Json.Nodes;
using CustodyChain.Web.Application.Common;
using CustodyChain.Web.Application.VerifiableCredentials;
using CustodyChain.Web.Services.Ledger;

namespace CustodyChain.Web.Application.Pericias;

public sealed class AprovarDesignacaoPericiaUseCase(
    IDesignacaoPericiaStore store,
    CriarVcPermissao criarVcPermissao,
    IServicoLedger ledger,
    IClock clock) : IAprovarDesignacaoPericia
{
    public static readonly IReadOnlyList<string> OperacoesAutorizadas =
    [
        "PERICIA_RECEBER",
        "LACRE_ROMPER",
        "LAUDO_EMITIR",
        "AMOSTRA_FRACIONAR",
        "AMOSTRA_UNIFICAR",
        "AMOSTRA_CONSUMIR",
        "AMOSTRA_EXAURIR"
    ];

    public Task<IReadOnlyList<SolicitacaoDesignacaoPericiaResumo>> ListarPendentesAsync(
        long aprovadorId,
        CancellationToken cancellationToken = default)
    {
        ValidarAtor(aprovadorId);
        return store.ListarPendentesAsync(aprovadorId, cancellationToken);
    }

    public async Task<SolicitacaoDesignacaoPericiaDetalhe> ObterDetalheAsync(
        long periciaId,
        long aprovadorId,
        CancellationToken cancellationToken = default)
    {
        ValidarIdentificadores(periciaId, aprovadorId);
        return await store.ObterDetalheAsync(periciaId, aprovadorId, cancellationToken)
            ?? throw new RecursoDesignacaoPericiaNaoEncontradoException(
                "Solicitação de perícia não encontrada ou não está disponível para aprovação.");
    }

    public async Task<PreparacaoAprovacaoDesignacaoPericia> PrepararAsync(
        PrepararAprovacaoDesignacaoPericiaCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidarIdentificadores(command.PericiaId, command.AprovadorId);
        var agora = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        if (command.ValidaAte is not null && command.ValidaAte <= agora)
            throw new ValidacaoDesignacaoPericiaException(
                "A validade da credencial deve ser posterior à emissão.", nameof(command.ValidaAte));

        var contexto = await store.PrepararAprovacaoAsync(
            new PreparacaoCredencialDesignacaoPericia(
                command.PericiaId,
                command.AprovadorId,
                $"urn:uuid:{Guid.NewGuid()}",
                agora,
                command.ValidaAte),
            cancellationToken) ?? throw new RecursoDesignacaoPericiaNaoEncontradoException(
                "Solicitação de perícia não encontrada ou não está disponível para aprovação.");

        var vc = CriarCredencial(contexto);
        return new PreparacaoAprovacaoDesignacaoPericia(
            vc.Credencial,
            contexto.DidEmissor,
            contexto.RotuloEvidencia);
    }

    public async Task<ResultadoAprovacaoDesignacaoPericia> ConcluirAsync(
        ConcluirAprovacaoDesignacaoPericiaCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidarIdentificadores(command.PericiaId, command.AprovadorId);
        var contexto = await store.ObterContextoAprovacaoAsync(
            command.PericiaId,
            command.AprovadorId,
            cancellationToken) ?? throw new RecursoDesignacaoPericiaNaoEncontradoException(
                "Aprovação pendente não encontrada para esta perícia.");

        var esperada = CriarCredencial(contexto);
        if (!CredencialCorresponde(esperada.Credencial, command.Credencial))
            throw new ValidacaoDesignacaoPericiaException(
                "A VC assinada não corresponde à aprovação preparada.");

        string identificador;
        try
        {
            identificador = await ledger.EmitirCredencialPermissaoV2Async(
                new CredencialPermissaoV2Dto(command.Credencial), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new IndisponibilidadeLedgerDesignacaoPericiaException(
                "Não foi possível confirmar a VC no ledger. Reenvie a mesma prova.", exception);
        }

        if (!string.Equals(identificador, contexto.IdentificadorCredencial, StringComparison.Ordinal))
            throw new ConflitoDesignacaoPericiaException(
                "O ledger confirmou um identificador diferente da credencial preparada.");

        if (!contexto.Concluida)
        {
            await store.ConcluirAprovacaoAsync(
                contexto.PericiaId,
                command.AprovadorId,
                contexto.IdentificadorCredencial,
                clock.UtcNow,
                cancellationToken);
        }

        return new ResultadoAprovacaoDesignacaoPericia(
            contexto.PericiaId,
            contexto.RotuloEvidencia,
            contexto.PeritoNome);
    }

    private VcPermissaoSemAssinatura CriarCredencial(ContextoAprovacaoDesignacaoPericia contexto) =>
        criarVcPermissao.Executar(new CriarVcPermissaoInput(
            contexto.DidEmissor,
            contexto.DidPerito,
            "PERITO",
            contexto.ProcessoId,
            contexto.ValidaAte,
            contexto.IdentificadorCredencial,
            contexto.EmitidaEm,
            contexto.VestigioId,
            OperacoesAutorizadas));

    private static bool CredencialCorresponde(JsonElement semAssinatura, JsonElement assinada)
    {
        if (assinada.ValueKind != JsonValueKind.Object || !assinada.TryGetProperty("proof", out _))
            return false;

        var recebida = JsonNode.Parse(assinada.GetRawText())?.AsObject();
        recebida?.Remove("proof");
        var esperada = JsonNode.Parse(semAssinatura.GetRawText());
        return JsonNode.DeepEquals(esperada, recebida);
    }

    private static void ValidarIdentificadores(long periciaId, long aprovadorId)
    {
        if (periciaId <= 0)
            throw new ValidacaoDesignacaoPericiaException("Perícia inválida.");
        ValidarAtor(aprovadorId);
    }

    private static void ValidarAtor(long aprovadorId)
    {
        if (aprovadorId <= 0)
            throw new ValidacaoDesignacaoPericiaException("A identidade autenticada é inválida.");
    }
}
