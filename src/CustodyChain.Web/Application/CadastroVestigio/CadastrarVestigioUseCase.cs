using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustodyChain.Web.Application.Common;

namespace CustodyChain.Web.Application.CadastroVestigio;

public sealed class CadastrarVestigioUseCase(
    ICadastroVestigioStore store,
    IClock clock,
    IGeradorIdentificadorCredencial geradorIdentificadorCredencial) : ICadastrarVestigio
{
    public async Task<ResultadoCadastroVestigio> ExecutarAsync(
        CadastrarVestigioCommand command,
        CancellationToken cancellationToken = default)
    {
        var dados = Normalizar(command);
        var processo = await ValidarPrecondicoesAsync(dados, cancellationToken);

        var ator = await store.ObterAtorAtivoAsync(dados.CriadorId, cancellationToken)
            ?? throw new AtorCadastroVestigioNaoAutorizadoException("O interveniente autenticado não está ativo.");

        var criadoEm = clock.UtcNow;
        var payloadJson = CriarPayloadJson(dados, ator.Did, processo.Numero);
        var payloadHashSha256 = CalcularHash(payloadJson);
        var credencialId = geradorIdentificadorCredencial.GerarCoC();

        var vestigioId = await store.PersistirAsync(
            new CadastroVestigioPendente(
                dados.RotuloEvidencia,
                dados.RotuloConjunto,
                dados.NumeroEvidencia,
                dados.ProcessoId,
                dados.TipoVestigioId,
                dados.Descricao,
                dados.CriadorId,
                dados.LocalColeta ?? string.Empty,
                dados.DataHoraColeta,
                dados.MetodoColeta,
                dados.HouveIntercorrencia,
                dados.DescricaoIntercorrencia,
                dados.NumeroLacre,
                criadoEm,
                payloadJson,
                payloadHashSha256,
                credencialId,
                ator.Did),
            cancellationToken);

        return new ResultadoCadastroVestigio(vestigioId, dados.RotuloEvidencia, AncoragemPendente: true);
    }

    private async Task<ProcessoCadastroVestigio> ValidarPrecondicoesAsync(CadastrarVestigioCommand command, CancellationToken cancellationToken)
    {
        if (await store.RotuloEvidenciaExisteAsync(command.RotuloEvidencia, cancellationToken))
        {
            throw new ConflitoCadastroVestigioException(
                "Já existe um vestígio com este rótulo de evidência.",
                nameof(command.RotuloEvidencia));
        }

        if (await store.NumeroLacreExisteAsync(command.NumeroLacre, cancellationToken))
        {
            throw new ConflitoCadastroVestigioException(
                "Já existe um lacre com este número.",
                nameof(command.NumeroLacre));
        }

        var processo = await store.ObterProcessoAtivoAsync(command.ProcessoId, cancellationToken)
            ?? throw new RecursoCadastroVestigioNaoEncontradoException("O processo selecionado não existe ou não está ativo.");

        if (!await store.TipoVestigioExisteAsync(command.TipoVestigioId, cancellationToken))
        {
            throw new RecursoCadastroVestigioNaoEncontradoException("O tipo de vestígio selecionado não existe.");
        }

        return processo;
    }

    private static CadastrarVestigioCommand Normalizar(CadastrarVestigioCommand command)
    {
        var rotuloEvidencia = ExigirTexto(command.RotuloEvidencia, nameof(command.RotuloEvidencia));
        var rotuloConjunto = ExigirTexto(command.RotuloConjunto, nameof(command.RotuloConjunto));
        var descricao = ExigirTexto(command.Descricao, nameof(command.Descricao));
        var numeroLacre = ExigirTexto(command.NumeroLacre, nameof(command.NumeroLacre));

        if (command.ProcessoId <= 0)
        {
            throw new ValidacaoCadastroVestigioException("Informe um processo válido.", nameof(command.ProcessoId));
        }

        if (command.TipoVestigioId <= 0)
        {
            throw new ValidacaoCadastroVestigioException("Informe um tipo de vestígio válido.", nameof(command.TipoVestigioId));
        }

        if (command.CriadorId <= 0)
        {
            throw new AtorCadastroVestigioNaoAutorizadoException("A identidade autenticada é inválida.");
        }

        if (command.HouveIntercorrencia && string.IsNullOrWhiteSpace(command.DescricaoIntercorrencia))
        {
            throw new ValidacaoCadastroVestigioException(
                "Descreva a intercorrência informada.",
                nameof(command.DescricaoIntercorrencia));
        }

        return command with
        {
            RotuloEvidencia = rotuloEvidencia,
            RotuloConjunto = rotuloConjunto,
            NumeroEvidencia = Limpar(command.NumeroEvidencia),
            Descricao = descricao,
            LocalColeta = Limpar(command.LocalColeta),
            MetodoColeta = Limpar(command.MetodoColeta),
            NumeroLacre = numeroLacre,
            DescricaoIntercorrencia = command.HouveIntercorrencia
                ? ExigirTexto(command.DescricaoIntercorrencia, nameof(command.DescricaoIntercorrencia))
                : null,
            DataHoraColeta = command.DataHoraColeta.ToUniversalTime()
        };
    }

    private static string CriarPayloadJson(CadastrarVestigioCommand command, string didCriador, string numeroProcesso)
    {
        var lacreDigital = new
        {
            NE = command.NumeroEvidencia,
            CRI = didCriador,
            DE = command.Descricao,
            NC = numeroProcesso,
            DH = command.DataHoraColeta,
            RE = command.RotuloEvidencia,
            RC = command.RotuloConjunto,
            NumeroLacre = command.NumeroLacre,
        };

        return JsonSerializer.Serialize(lacreDigital);
    }

    private static string CalcularHash(string payloadJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

    private static string ExigirTexto(string? valor, string campo)
    {
        var texto = Limpar(valor);
        return texto ?? throw new ValidacaoCadastroVestigioException("Informe um valor.", campo);
    }

    private static string? Limpar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
