using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Tests.TestSupport;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "url-shortener-tests", Guid.NewGuid().ToString("N"));

    public SqliteTestDatabase() => Directory.CreateDirectory(_directory);

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(_directory, "links.db"),
        ForeignKeys = true,
        Pooling = false
    }.ToString();

    public async Task<LinkDbContext> CreateContextAsync()
    {
        Directory.CreateDirectory(_directory);
        var options = new DbContextOptionsBuilder<LinkDbContext>().UseSqlite(ConnectionString).Options;
        var context = new LinkDbContext(options);
        await context.Database.MigrateAsync();
        return context;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}