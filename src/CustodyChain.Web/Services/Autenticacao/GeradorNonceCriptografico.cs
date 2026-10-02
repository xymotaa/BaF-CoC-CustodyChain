using System.Security.Cryptography;
using CustodyChain.Web.Application.Autenticacao;

namespace CustodyChain.Web.Services.Autenticacao;

public sealed class GeradorNonceCriptografico : IGeradorNonce
{
    public byte[] Gerar(int quantidadeBytes) => RandomNumberGenerator.GetBytes(quantidadeBytes);
}
