namespace UrlShortener.Core.Exceptions
{
    public sealed class ShortCodeConflictException : Exception
    {
        public ShortCodeConflictException(string shortCode, Exception? innerException = null)
            : base($"The short code '{shortCode}' is already in use.", innerException)
        {
            ShortCode = shortCode;
        }

        public string ShortCode { get; }
    }
}