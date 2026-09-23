using System.Text.Json;

namespace CustodyChain.Web.Services.Armazenamento;

public class ServicoArmazenamentoIpfs(HttpClient httpClient) : IServicoArmazenamentoArquivos
{
    public async Task<ArquivoArmazenado> ArmazenarAsync(Stream conteudo, string nomeArquivo, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(conteudo);
        form.Add(streamContent, "file", nomeArquivo);

        var resposta = await httpClient.PostAsync("/api/v0/add?cid-version=1", form, cancellationToken);
        resposta.EnsureSuccessStatusCode();

        var json = await resposta.Content.ReadAsStringAsync(cancellationToken);
        using var documento = JsonDocument.Parse(json);
        var cid = documento.RootElement.GetProperty("Hash").GetString()!;
        var tamanho = long.Parse(documento.RootElement.GetProperty("Size").GetString()!);

        return new ArquivoArmazenado(cid, tamanho);
    }

    public async Task<Stream> RecuperarAsync(string cid, CancellationToken cancellationToken = default)
    {
        var resposta = await httpClient.PostAsync($"/api/v0/cat?arg={Uri.EscapeDataString(cid)}", content: null, cancellationToken);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStreamAsync(cancellationToken);
    }
}
