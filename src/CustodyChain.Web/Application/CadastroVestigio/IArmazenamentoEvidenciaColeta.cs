namespace CustodyChain.Web.Application.CadastroVestigio;

public interface IArmazenamentoEvidenciaColeta
{
    Task<EvidenciaColetaArmazenada> ArmazenarAsync(
        byte[] conteudo,
        string nomeArquivo,
        CancellationToken cancellationToken = default);
}

public sealed record EvidenciaColetaArmazenada(string Cid, long TamanhoBytes);
