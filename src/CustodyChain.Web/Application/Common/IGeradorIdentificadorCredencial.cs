namespace CustodyChain.Web.Application.Common;

public interface IGeradorIdentificadorCredencial
{
    string GerarCoC();
}

public sealed class GeradorIdentificadorCredencial : IGeradorIdentificadorCredencial
{
    public string GerarCoC() => $"cred-coc-{Guid.NewGuid():N}";
}
