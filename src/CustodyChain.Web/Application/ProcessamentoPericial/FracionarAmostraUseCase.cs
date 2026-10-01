using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class FracionarAmostraUseCase(
    IFracionamentoAmostraStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IFracionarAmostra
{
    public async Task<ResultadoFracionamentoAmostra> ExecutarAsync(
        FracionarAmostraCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(dados.PericiaId, dados.PeritoId, cancellationToken)
            ?? throw new RecursoFracionamentoAmostraNaoEncontradoException(
                "Perícia não encontrada ou lacre ainda não rompido.");
        if (await store.RotuloEvidenciaExisteAsync(dados.RotuloEvidenciaResultante!, cancellationToken))
            throw new ConflitoFracionamentoAmostraException(
                "Já existe um vestígio com este rótulo de evidência.", nameof(command.RotuloEvidenciaResultante));

        var executadoEm = clock.UtcNow;
        var payloadJson = JsonSerializer.Serialize(new
        {
            REOrigem = contexto.RotuloEvidenciaOrigem,
            REResultante = dados.RotuloEvidenciaResultante,
            RC = contexto.RotuloConjunto,
            dados.Justificativa,
        });

        await store.PersistirAsync(new FracionamentoAmostraPendente(
            contexto.PericiaId,
            dados.PeritoId,
            contexto.RotuloEvidenciaOrigem,
            contexto.RotuloConjunto,
            contexto.ProcessoId,
            contexto.TipoVestigioId,
            contexto.HashSha256,
            dados.RotuloEvidenciaResultante!,
            dados.DescricaoResultante!,
            dados.QuantidadeDescrita,
            dados.Justificativa!,
            executadoEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoFracionamentoAmostra(
            contexto.RotuloEvidenciaOrigem, dados.RotuloEvidenciaResultante!, AncoragemPendente: true);
    }

    private static FracionarAmostraCommand Normalizar(FracionarAmostraCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorFracionamentoAmostraNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoFracionamentoAmostraException("Perícia inválida.", nameof(command.PericiaId));
        if (string.IsNullOrWhiteSpace(command.RotuloEvidenciaResultante))
            throw new ValidacaoFracionamentoAmostraException("Informe o rótulo de evidência resultante.", nameof(command.RotuloEvidenciaResultante));
        if (string.IsNullOrWhiteSpace(command.DescricaoResultante))
            throw new ValidacaoFracionamentoAmostraException("Informe a descrição resultante.", nameof(command.DescricaoResultante));
        if (string.IsNullOrWhiteSpace(command.Justificativa))
            throw new ValidacaoFracionamentoAmostraException("Informe a justificativa.", nameof(command.Justificativa));

        return command with
        {
            RotuloEvidenciaResultante = command.RotuloEvidenciaResultante.Trim(),
            DescricaoResultante = command.DescricaoResultante.Trim(),
            QuantidadeDescrita = Limpar(command.QuantidadeDescrita),
            Justificativa = command.Justificativa.Trim(),
        };
    }

    private static string CalcularHash(string conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
