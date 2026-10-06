using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.Pericias;

public sealed class SolicitarDesignacaoPericiaUseCase(
    IDesignacaoPericiaStore store,
    IClock clock) : ISolicitarDesignacaoPericia
{
    private static readonly IReadOnlySet<string> PrioridadesPermitidas =
        new HashSet<string>(StringComparer.Ordinal) { "NORMAL", "URGENTE" };

    public async Task<ResultadoSolicitacaoDesignacaoPericia> ExecutarAsync(
        SolicitarDesignacaoPericiaCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoSolicitacaoAsync(
            dados.VestigioId!.Value,
            dados.PeritoId!.Value,
            dados.SolicitanteId,
            cancellationToken) ?? throw new RecursoDesignacaoPericiaNaoEncontradoException(
                "Vestígio, perito ou solicitante não está disponível para a solicitação.");

        var periciaId = await store.PersistirSolicitacaoAsync(
            new SolicitacaoDesignacaoPericiaPendente(
                contexto.VestigioId,
                contexto.ProcessoId,
                contexto.PeritoId,
                dados.SolicitanteId,
                dados.AreaPericial,
                dados.Prioridade!,
                clock.UtcNow),
            cancellationToken);

        return new ResultadoSolicitacaoDesignacaoPericia(
            periciaId,
            contexto.RotuloEvidencia,
            contexto.PeritoNome);
    }

    private static SolicitarDesignacaoPericiaCommand Normalizar(SolicitarDesignacaoPericiaCommand command)
    {
        if (command.SolicitanteId <= 0)
            throw new ValidacaoDesignacaoPericiaException("A identidade autenticada é inválida.");
        if (command.VestigioId is null or <= 0)
            throw new ValidacaoDesignacaoPericiaException("Selecione o vestígio.", nameof(command.VestigioId));
        if (command.PeritoId is null or <= 0)
            throw new ValidacaoDesignacaoPericiaException("Selecione o perito.", nameof(command.PeritoId));
        if (string.IsNullOrWhiteSpace(command.Prioridade)
            || !PrioridadesPermitidas.Contains(command.Prioridade.Trim().ToUpperInvariant()))
            throw new ValidacaoDesignacaoPericiaException("Prioridade inválida.", nameof(command.Prioridade));

        var area = string.IsNullOrWhiteSpace(command.AreaPericial) ? null : command.AreaPericial.Trim();
        if (area?.Length > 60)
            throw new ValidacaoDesignacaoPericiaException(
                "A área pericial deve ter no máximo 60 caracteres.", nameof(command.AreaPericial));

        return command with
        {
            AreaPericial = area,
            Prioridade = command.Prioridade.Trim().ToUpperInvariant()
        };
    }
}
