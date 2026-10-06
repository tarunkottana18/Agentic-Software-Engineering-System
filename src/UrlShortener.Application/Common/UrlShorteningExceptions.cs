namespace UrlShortener.Application.Common
{
    public sealed class InvalidUrlException : Exception
    {
        public InvalidUrlException(string message) : base(message) { }
    }

    public sealed class ShortCodeGenerationException : Exception
    {
        public ShortCodeGenerationException(string message, Exception? innerException = null)
            : base(message, innerException) { }
    }
}