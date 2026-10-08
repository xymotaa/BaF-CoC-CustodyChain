using CustodyChain.Web.Application.CadastroVestigio;

namespace CustodyChain.Web.Services.Armazenamento;

public sealed class ArmazenamentoEvidenciaColetaIpfs(
    IServicoArmazenamentoArquivos armazenamento) : IArmazenamentoEvidenciaColeta
{
    public async Task<EvidenciaColetaArmazenada> ArmazenarAsync(
        byte[] conteudo,
        string nomeArquivo,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(conteudo, writable: false);
        var arquivo = await armazenamento.ArmazenarAsync(stream, nomeArquivo, cancellationToken);
        return new EvidenciaColetaArmazenada(arquivo.Cid, arquivo.TamanhoBytes);
    }
}
