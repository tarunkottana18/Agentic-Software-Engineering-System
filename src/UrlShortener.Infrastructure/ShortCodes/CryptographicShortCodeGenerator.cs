using System.Security.Cryptography;
using UrlShortener.Application.Common;

namespace UrlShortener.Infrastructure.ShortCodes
{
    public sealed class CryptographicShortCodeGenerator : IShortCodeGenerator
    {
        private const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        private const int CodeLength = 7;

        public string Generate()
        {
            return string.Create(CodeLength, Characters, static (code, characters) =>
            {
                for (var index = 0; index < code.Length; index++)
                {
                    code[index] = characters[RandomNumberGenerator.GetInt32(characters.Length)];
                }
            });
        }
    }
}