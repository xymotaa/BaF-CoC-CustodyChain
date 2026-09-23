namespace CustodyChain.Web.Services.Armazenamento;

public record ArquivoArmazenado(string Cid, long TamanhoBytes);

/// <summary>
/// Armazenamento off-chain de anexos (P-01 do documento de contexto,
/// resolvido via IPFS privado local). O identificador retornado é o CID
/// do IPFS, gravado em Anexo.CaminhoRelativo.
/// </summary>
public interface IServicoArmazenamentoArquivos
{
    Task<ArquivoArmazenado> ArmazenarAsync(Stream conteudo, string nomeArquivo, CancellationToken cancellationToken = default);
    Task<Stream> RecuperarAsync(string cid, CancellationToken cancellationToken = default);
}
