using System.Security.Cryptography;
using UrlShortener.Application.Interfaces;

namespace UrlShortener.Infrastructure.ShortCodes;

public sealed class CryptographicShortCodeGenerator : IShortCodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public string Generate()
    {
        Span<char> code = stackalloc char[7];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(code);
    }
}