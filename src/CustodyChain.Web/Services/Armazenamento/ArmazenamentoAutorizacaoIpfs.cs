using CustodyChain.Web.Application.DestinacaoFinal;

namespace CustodyChain.Web.Services.Armazenamento;

public sealed class ArmazenamentoAutorizacaoIpfs(
    IServicoArmazenamentoArquivos armazenamento) : IArmazenamentoAutorizacao
{
    public async Task<AutorizacaoArmazenada> ArmazenarAsync(
        byte[] conteudo,
        string nomeArquivo,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(conteudo, writable: false);
        var arquivo = await armazenamento.ArmazenarAsync(stream, nomeArquivo, cancellationToken);
        return new AutorizacaoArmazenada(arquivo.Cid, arquivo.TamanhoBytes);
    }
}
