using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Repositories;

public sealed class LinkRepository(LinkDbContext context) : ILinkRepository
{
    public Task<ShortLink?> FindByOriginalUrlAsync(string originalUrl, CancellationToken cancellationToken) =>
        context.Links.AsNoTracking().SingleOrDefaultAsync(link => link.OriginalUrl == originalUrl, cancellationToken);

    public Task<ShortLink?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Links.AsNoTracking().SingleOrDefaultAsync(link => link.Code == code, cancellationToken);

    public Task<LinkAnalytics?> GetAnalyticsAsync(string code, CancellationToken cancellationToken) =>
        context.Links.AsNoTracking()
            .Where(link => link.Code == code)
            .Select(link => new LinkAnalytics(link.Code, link.ClickCount))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<LinkCreateStatus> TryCreateAsync(ShortLink link, CancellationToken cancellationToken)
    {
        context.Links.Add(link);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return LinkCreateStatus.Created;
        }
        catch (DbUpdateException exception) when (GetUniqueConflict(exception) is not null)
        {
            context.Entry(link).State = EntityState.Detached;
            return GetUniqueConflict(exception)!.Value;
        }
    }

    public async Task<string?> IncrementClickCountAndGetOriginalUrlAsync(string code, CancellationToken cancellationToken)
    {
        var originalUrl = await context.Links.AsNoTracking()
            .Where(link => link.Code == code)
            .Select(link => link.OriginalUrl)
            .SingleOrDefaultAsync(cancellationToken);

        if (originalUrl is null)
        {
            return null;
        }

        var updated = await context.Links
            .Where(link => link.Code == code)
            .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.ClickCount, link => link.ClickCount + 1), cancellationToken);

        return updated == 1 ? originalUrl : null;
    }

    private static LinkCreateStatus? GetUniqueConflict(DbUpdateException exception)
    {
        var sqliteException = FindSqliteException(exception);
        if (sqliteException is null || sqliteException.SqliteExtendedErrorCode != 2067)
        {
            return null;
        }

        if (sqliteException.Message.Contains("Links.Code", StringComparison.Ordinal))
        {
            return LinkCreateStatus.CodeConflict;
        }

        if (sqliteException.Message.Contains("Links.OriginalUrl", StringComparison.Ordinal))
        {
            return LinkCreateStatus.OriginalUrlConflict;
        }

        return null;
    }

    private static SqliteException? FindSqliteException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqliteException)
            {
                return sqliteException;
            }
        }

        return null;
    }
}