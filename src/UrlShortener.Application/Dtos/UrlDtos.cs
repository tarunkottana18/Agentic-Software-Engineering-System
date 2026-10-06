namespace UrlShortener.Application.Dtos
{
    public record UrlShortenRequest(string OriginalUrl);
    public record UrlShortenResponse(string ShortCode, string ShortUrl);
    public record UrlRedirectResponse(string OriginalUrl);
    public record UrlAnalyticsResponse(string ShortCode, string OriginalUrl, DateTime CreatedAt, int ClickCount);
}
