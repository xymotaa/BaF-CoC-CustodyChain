namespace CustodyChain.Web.Application.CadastroVestigio;

public interface ICadastroVestigioOpcoesQuery
{
    Task<IReadOnlyList<OpcaoProcessoCadastroVestigio>> ListarProcessosAtivosAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpcaoTipoCadastroVestigio>> ListarTiposAsync(CancellationToken cancellationToken = default);
}

public sealed record OpcaoProcessoCadastroVestigio(long Id, string Numero, string? NomeOperacao);
public sealed record OpcaoTipoCadastroVestigio(short Id, string Descricao, string Categoria);
