using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.ProcessamentoPericial;

public sealed class UnificarAmostrasUseCase(IUnificacaoAmostrasStore store, IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : IUnificarAmostras
{
    public async Task<ResultadoUnificacaoAmostras> ExecutarAsync(UnificarAmostrasCommand command, CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var contexto = await store.ObterContextoAsync(dados.PericiaId, dados.PeritoId, cancellationToken)
            ?? throw new RecursoUnificacaoAmostrasNaoEncontradoException("Perícia não encontrada ou lacre ainda não rompido.");
        var ids = OutrosIds(dados).Append(contexto.VestigioPrincipalId).Distinct().ToList();
        var origens = await store.ObterOrigensAsync(ids, cancellationToken);
        if (origens.Count != ids.Count || origens.Select(o => o.RotuloConjunto).Distinct().Count() != 1)
            throw new ValidacaoUnificacaoAmostrasException(
                "Os vestígios informados precisam existir e compartilhar o mesmo rótulo de conjunto (RN18).",
                nameof(command.OutrosVestigiosOrigemIds));
        if (await store.RotuloEvidenciaExisteAsync(dados.RotuloEvidenciaResultante!, cancellationToken))
            throw new ConflitoUnificacaoAmostrasException("Já existe um vestígio com este rótulo de evidência.",
                nameof(command.RotuloEvidenciaResultante));

        var executadoEm = clock.UtcNow;
        var hashCombinado = CalcularHashCombinado(origens);
        var payloadJson = JsonSerializer.Serialize(new
        {
            REsOrigem = origens.Select(o => o.RotuloEvidencia), REResultante = dados.RotuloEvidenciaResultante,
            RC = origens[0].RotuloConjunto, dados.Justificativa,
        });
        await store.PersistirAsync(new UnificacaoAmostrasPendente(contexto.PericiaId, dados.PeritoId, origens,
            dados.RotuloEvidenciaResultante!, dados.DescricaoResultante!, dados.Justificativa!, hashCombinado, executadoEm,
            payloadJson, CalcularHash(payloadJson), geradorIdentificadorCredencial.GerarCoC(), contexto.DidPerito), cancellationToken);
        return new ResultadoUnificacaoAmostras(origens.Count, dados.RotuloEvidenciaResultante!, true);
    }

    private static UnificarAmostrasCommand Normalizar(UnificarAmostrasCommand command)
    {
        if (command.PeritoId <= 0) throw new AtorUnificacaoAmostrasNaoAutorizadoException("A identidade autenticada é inválida.");
        if (command.PericiaId <= 0) throw new ValidacaoUnificacaoAmostrasException("Perícia inválida.", nameof(command.PericiaId));
        var outrosIds = (command.OutrosVestigiosOrigemIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(valor => long.TryParse(valor, out var id) && id > 0 ? id : (long?)null).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (outrosIds.Count == 0) throw new ValidacaoUnificacaoAmostrasException("Informe ao menos um outro vestígio de origem.", nameof(command.OutrosVestigiosOrigemIds));
        if (string.IsNullOrWhiteSpace(command.RotuloEvidenciaResultante)) throw new ValidacaoUnificacaoAmostrasException("Informe o rótulo de evidência resultante.", nameof(command.RotuloEvidenciaResultante));
        if (string.IsNullOrWhiteSpace(command.DescricaoResultante)) throw new ValidacaoUnificacaoAmostrasException("Informe a descrição resultante.", nameof(command.DescricaoResultante));
        if (string.IsNullOrWhiteSpace(command.Justificativa)) throw new ValidacaoUnificacaoAmostrasException("Informe a justificativa.", nameof(command.Justificativa));
        return command with { OutrosVestigiosOrigemIds = string.Join(',', outrosIds), RotuloEvidenciaResultante = command.RotuloEvidenciaResultante.Trim(), DescricaoResultante = command.DescricaoResultante.Trim(), Justificativa = command.Justificativa.Trim() };
    }

    private static List<long> OutrosIds(UnificarAmostrasCommand command) => command.OutrosVestigiosOrigemIds!.Split(',').Select(long.Parse).ToList();
    private static string? CalcularHashCombinado(IEnumerable<OrigemUnificacaoAmostra> origens)
    {
        var hashes = origens.Where(o => !string.IsNullOrEmpty(o.HashSha256)).OrderBy(o => o.RotuloEvidencia).Select(o => o.HashSha256!);
        var combinado = string.Concat(hashes);
        return combinado.Length == 0 ? null : CalcularHash(combinado);
    }
    private static string CalcularHash(string valor) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
}
