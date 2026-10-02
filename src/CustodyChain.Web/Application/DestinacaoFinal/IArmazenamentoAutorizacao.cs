namespace CustodyChain.Web.Application.DestinacaoFinal;

public interface IArmazenamentoAutorizacao
{
    Task<AutorizacaoArmazenada> ArmazenarAsync(
        byte[] conteudo,
        string nomeArquivo,
        CancellationToken cancellationToken = default);
}

public sealed record AutorizacaoArmazenada(string Cid, long TamanhoBytes);
