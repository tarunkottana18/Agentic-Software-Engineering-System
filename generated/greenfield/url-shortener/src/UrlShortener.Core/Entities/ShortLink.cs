namespace UrlShortener.Core.Entities;

public sealed class ShortLink
{
    private ShortLink()
    {
    }

    public ShortLink(string code, string originalUrl)
    {
        Code = code;
        OriginalUrl = originalUrl;
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string OriginalUrl { get; private set; } = string.Empty;
    public int ClickCount { get; private set; }
}