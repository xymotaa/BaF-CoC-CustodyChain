using System.Security.Cryptography;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.DestinacaoFinal;

public sealed class SolicitarDestinacaoUseCase(
    IDestinacaoFinalStore store,
    IArmazenamentoAutorizacao armazenamento,
    IClock clock) : ISolicitarDestinacao
{
    private static readonly IReadOnlySet<string> TiposPermitidos = new HashSet<string>(StringComparer.Ordinal)
    {
        "DESCARTE",
        "RESTITUICAO",
    };

    public async Task<ResultadoSolicitacaoDestinacao> ExecutarAsync(
        SolicitarDestinacaoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoSolicitacaoAsync(
            dados.VestigioId!.Value,
            dados.SolicitanteId,
            cancellationToken) ?? throw new RecursoDestinacaoFinalNaoEncontradoException(
                "Vestígio não encontrado ou não está em um estado elegível para destinação final.");

        var autorizacao = await armazenamento.ArmazenarAsync(
            dados.ConteudoAutorizacao!,
            dados.NomeArquivoAutorizacao!,
            cancellationToken);
        var solicitadoEm = clock.UtcNow;

        await store.PersistirSolicitacaoAsync(new SolicitacaoDestinacaoPendente(
            contexto.VestigioId,
            dados.SolicitanteId,
            dados.Tipo!,
            dados.DidMagistrado!,
            dados.NomeArquivoAutorizacao!,
            autorizacao.Cid,
            autorizacao.TamanhoBytes,
            CalcularHash(dados.ConteudoAutorizacao!),
            Limpar(dados.Observacao),
            solicitadoEm), cancellationToken);

        return new ResultadoSolicitacaoDestinacao(contexto.RotuloEvidencia);
    }

    private static SolicitarDestinacaoCommand Normalizar(SolicitarDestinacaoCommand command)
    {
        if (command.SolicitanteId <= 0)
            throw new AtorDestinacaoFinalNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.VestigioId is null or <= 0)
            throw new ValidacaoDestinacaoFinalException("Selecione o vestígio.", nameof(command.VestigioId));
        if (string.IsNullOrWhiteSpace(command.Tipo) || !TiposPermitidos.Contains(command.Tipo))
            throw new ValidacaoDestinacaoFinalException("Tipo inválido.", nameof(command.Tipo));
        if (string.IsNullOrWhiteSpace(command.DidMagistrado))
            throw new ValidacaoDestinacaoFinalException("Informe o DID do magistrado que autorizou.", nameof(command.DidMagistrado));
        if (string.IsNullOrWhiteSpace(command.NomeArquivoAutorizacao)
            || command.ConteudoAutorizacao is not { Length: > 0 })
            throw new ValidacaoDestinacaoFinalException("Anexe o mandado judicial.", nameof(command.ConteudoAutorizacao));

        return command with
        {
            DidMagistrado = command.DidMagistrado.Trim(),
            NomeArquivoAutorizacao = command.NomeArquivoAutorizacao.Trim(),
        };
    }

    private static string CalcularHash(byte[] conteudo) => Convert.ToHexStringLower(SHA256.HashData(conteudo));

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
