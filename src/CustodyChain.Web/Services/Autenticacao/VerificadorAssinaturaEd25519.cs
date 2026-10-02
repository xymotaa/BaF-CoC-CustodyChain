using System.Runtime.InteropServices;
using CustodyChain.Web.Application.Autenticacao;

namespace CustodyChain.Web.Services.Autenticacao;

public sealed class VerificadorAssinaturaEd25519 : IVerificadorAssinaturaDid
{
    private const int TamanhoChavePublica = 32;
    private const int TamanhoAssinatura = 64;
    private static readonly byte[] PrefixoMulticodecEd25519 = [0xed, 0x01];

    static VerificadorAssinaturaEd25519()
    {
        if (SodiumInit() < 0)
        {
            throw new InvalidOperationException("Não foi possível inicializar a biblioteca libsodium.");
        }
    }

    public bool Verificar(
        MetodoVerificacaoDid metodo,
        ReadOnlySpan<byte> mensagem,
        string assinaturaBase64Url)
    {
        if (!string.Equals(metodo.Tipo, "Multikey", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var chaveMulticodec = Base58Btc.DecodificarMultibase(metodo.PublicKeyMultibase);
            if (chaveMulticodec.Length != PrefixoMulticodecEd25519.Length + TamanhoChavePublica
                || !chaveMulticodec.AsSpan(0, 2).SequenceEqual(PrefixoMulticodecEd25519))
            {
                return false;
            }

            var assinatura = DecodificarBase64Url(assinaturaBase64Url);
            if (assinatura.Length != TamanhoAssinatura)
            {
                return false;
            }

            var mensagemArray = mensagem.ToArray();
            var chavePublica = chaveMulticodec[2..];
            return CryptoSignVerifyDetached(
                assinatura,
                mensagemArray,
                (ulong)mensagemArray.Length,
                chavePublica) == 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] DecodificarBase64Url(string valor)
    {
        var base64 = valor.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    [DllImport("libsodium", EntryPoint = "sodium_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SodiumInit();

    [DllImport("libsodium", EntryPoint = "crypto_sign_verify_detached", CallingConvention = CallingConvention.Cdecl)]
    private static extern int CryptoSignVerifyDetached(
        byte[] signature,
        byte[] message,
        ulong messageLength,
        byte[] publicKey);

    private static class Base58Btc
    {
        private const string Alfabeto = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static byte[] DecodificarMultibase(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor) || valor[0] != 'z')
            {
                throw new FormatException("A chave pública não usa multibase base58btc.");
            }

            var bytes = new List<byte> { 0 };
            foreach (var caractere in valor.AsSpan(1))
            {
                var digito = Alfabeto.IndexOf(caractere);
                if (digito < 0)
                {
                    throw new FormatException("Chave pública multibase inválida.");
                }

                var transporte = digito;
                for (var indice = 0; indice < bytes.Count; indice++)
                {
                    var acumulado = bytes[indice] * 58 + transporte;
                    bytes[indice] = (byte)(acumulado & 0xff);
                    transporte = acumulado >> 8;
                }

                while (transporte > 0)
                {
                    bytes.Add((byte)(transporte & 0xff));
                    transporte >>= 8;
                }
            }

            var zeros = valor.AsSpan(1).IndexOfAnyExcept('1');
            if (zeros < 0)
            {
                zeros = valor.Length - 1;
            }

            while (bytes.Count > 1 && bytes[^1] == 0)
            {
                bytes.RemoveAt(bytes.Count - 1);
            }

            var resultado = new byte[zeros + bytes.Count];
            for (var indice = 0; indice < bytes.Count; indice++)
            {
                resultado[resultado.Length - 1 - indice] = bytes[indice];
            }

            return resultado;
        }
    }
}
