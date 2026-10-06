using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Entities;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Tests.TestSupport;

namespace UrlShortener.Tests;

public sealed class MigrationAndCollationTests
{
    [Fact]
    public async Task MigrationCreatesBinaryUniqueIndexesForCodeAndOriginalUrl()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = await database.CreateContextAsync();
        context.Links.AddRange(
            new ShortLink("AbC1234", "https://case.test/Path"),
            new ShortLink("abc1234", "https://case.test/path"));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Links.CountAsync());
        Assert.Contains("202610060001_InitialLinks", await context.Database.GetAppliedMigrationsAsync());
        var collations = await context.Database.SqlQueryRaw<string>(
            "SELECT coll FROM pragma_index_xinfo('IX_Links_Code') WHERE key = 1").ToListAsync();
        Assert.Contains("BINARY", collations);
    }
}