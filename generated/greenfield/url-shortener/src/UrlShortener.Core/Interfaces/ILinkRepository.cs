using UrlShortener.Core.Entities;

namespace UrlShortener.Core.Interfaces;

public enum LinkCreateStatus
{
    Created,
    CodeConflict,
    OriginalUrlConflict
}

public sealed record LinkAnalytics(string Code, int ClickCount);

public interface ILinkRepository
{
    Task<ShortLink?> FindByOriginalUrlAsync(string originalUrl, CancellationToken cancellationToken);
    Task<ShortLink?> FindByCodeAsync(string code, CancellationToken cancellationToken);
    Task<LinkAnalytics?> GetAnalyticsAsync(string code, CancellationToken cancellationToken);
    Task<LinkCreateStatus> TryCreateAsync(ShortLink link, CancellationToken cancellationToken);
    Task<string?> IncrementClickCountAndGetOriginalUrlAsync(string code, CancellationToken cancellationToken);
}