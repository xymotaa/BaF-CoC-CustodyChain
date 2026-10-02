using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class RegistrarConsumoOuExaurimentoUseCase(
    IConsumoOuExaurimentoStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IRegistrarConsumoOuExaurimento
{
    private static readonly IReadOnlySet<string> TiposPermitidos = new HashSet<string>(StringComparer.Ordinal)
    {
        "CONSUMO",
        "EXAURIMENTO",
    };

    public async Task<ResultadoConsumoOuExaurimento> ExecutarAsync(
        RegistrarConsumoOuExaurimentoCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(dados.PericiaId, dados.PeritoId, cancellationToken)
            ?? throw new RecursoConsumoOuExaurimentoNaoEncontradoException(
                "Perícia não encontrada ou lacre ainda não rompido.");

        var executadoEm = clock.UtcNow;
        var payloadJson = JsonSerializer.Serialize(new
        {
            RE = contexto.RotuloEvidencia,
            dados.QuantidadeDescrita,
            dados.Justificativa,
        });

        await store.PersistirAsync(new ConsumoOuExaurimentoPendente(
            contexto.PericiaId,
            dados.PeritoId,
            contexto.VestigioId,
            dados.Tipo!,
            dados.QuantidadeDescrita,
            dados.Justificativa!,
            executadoEm,
            payloadJson,
            CalcularHash(payloadJson),
            geradorIdentificadorCredencial.GerarCoC(),
            contexto.DidPerito), cancellationToken);

        return new ResultadoConsumoOuExaurimento(dados.Tipo!, contexto.RotuloEvidencia, AncoragemPendente: true);
    }

    private static RegistrarConsumoOuExaurimentoCommand Normalizar(RegistrarConsumoOuExaurimentoCommand command)
    {
        if (command.PeritoId <= 0)
            throw new AtorConsumoOuExaurimentoNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0)
            throw new ValidacaoConsumoOuExaurimentoException("Perícia inválida.", nameof(command.PericiaId));
        if (string.IsNullOrWhiteSpace(command.Tipo) || !TiposPermitidos.Contains(command.Tipo))
            throw new ValidacaoConsumoOuExaurimentoException("Informe consumo ou exaurimento.", nameof(command.Tipo));
        if (string.IsNullOrWhiteSpace(command.Justificativa))
            throw new ValidacaoConsumoOuExaurimentoException("Informe a justificativa.", nameof(command.Justificativa));

        return command with
        {
            QuantidadeDescrita = Limpar(command.QuantidadeDescrita),
            Justificativa = command.Justificativa.Trim(),
        };
    }

    private static string CalcularHash(string conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
