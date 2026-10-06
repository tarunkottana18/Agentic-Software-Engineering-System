using Microsoft.EntityFrameworkCore;
using UrlShortener.Application.Features.Links;
using UrlShortener.Application.Interfaces;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;
using UrlShortener.Infrastructure.Repositories;
using UrlShortener.Tests.TestSupport;

namespace UrlShortener.Tests;

public sealed class CreateLinkHandlerTests
{
    [Fact]
    public async Task ConcurrentExactDuplicateCreationReturnsSingleWinnerWithoutCodeRetry()
    {
        await using var database = new SqliteTestDatabase();
        await using var context1 = await database.CreateContextAsync();
        await using var context2 = await database.CreateContextAsync();
        var barrier = new InitialLookupBarrier();
        var repository1 = new BarrierRepository(new LinkRepository(context1), barrier);
        var repository2 = new BarrierRepository(new LinkRepository(context2), barrier);
        var generator1 = new SequenceCodeGenerator("AbC1234");
        var generator2 = new SequenceCodeGenerator("xYz5678");
        var handler1 = new CreateLinkHandler(repository1, generator1);
        var handler2 = new CreateLinkHandler(repository2, generator2);
        const string originalUrl = "https://same.test/Exact";

        var firstTask = handler1.Handle(new CreateLinkCommand(originalUrl), CancellationToken.None);
        var secondTask = handler2.Handle(new CreateLinkCommand(originalUrl), CancellationToken.None);
        await barrier.BothInitialLookups;
        barrier.Release();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(results[0].Code, results[1].Code);
        Assert.Equal(1, generator1.Calls);
        Assert.Equal(1, generator2.Calls);
        Assert.Single(results, result => result.Reused);
        Assert.Equal(1, await context1.Links.CountAsync());
    }

    [Fact]
    public async Task CodeConflictRetriesButUrlConflictReturnsWinnerWithoutRetry()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = await database.CreateContextAsync();
        context.Links.Add(new ShortLink("Taken01", "https://existing.test/"));
        await context.SaveChangesAsync();
        var generator = new SequenceCodeGenerator("Taken01", "Free002");
        var handler = new CreateLinkHandler(new LinkRepository(context), generator);

        var result = await handler.Handle(new CreateLinkCommand("https://new.test/"), CancellationToken.None);

        Assert.False(result.Reused);
        Assert.Equal("Free002", result.Code);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task FiveActualCodeCollisionsReturnUnavailable()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = await database.CreateContextAsync();
        context.Links.Add(new ShortLink("Taken01", "https://existing.test/"));
        await context.SaveChangesAsync();
        var generator = new SequenceCodeGenerator(Enumerable.Repeat("Taken01", 5).ToArray());
        var handler = new CreateLinkHandler(new LinkRepository(context), generator);

        var result = await handler.Handle(new CreateLinkCommand("https://new.test/"), CancellationToken.None);

        Assert.True(result.IsUnavailable);
        Assert.Equal(5, generator.Calls);
    }

    [Fact]
    public async Task UnrelatedPersistenceFailureIsNotConvertedToCodeConflict()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = await database.CreateContextAsync();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE Links");
        var repository = new LinkRepository(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.TryCreateAsync(
            new ShortLink("NewCode", "https://failure.test/"), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidInputDoesNotGenerateCode()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = await database.CreateContextAsync();
        var generator = new SequenceCodeGenerator("AbC1234");
        var handler = new CreateLinkHandler(new LinkRepository(context), generator);

        var result = await handler.Handle(new CreateLinkCommand(" https://example.test"), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(0, generator.Calls);
    }

    private sealed class SequenceCodeGenerator(params string[] codes) : IShortCodeGenerator
    {
        private int _index;
        public int Calls => _index;
        public string Generate() => codes[Interlocked.Increment(ref _index) - 1];
    }

    private sealed class BarrierRepository(ILinkRepository inner, InitialLookupBarrier barrier) : ILinkRepository
    {
        private int _lookupCount;
        public async Task<ShortLink?> FindByOriginalUrlAsync(string originalUrl, CancellationToken cancellationToken)
        {
            var result = await inner.FindByOriginalUrlAsync(originalUrl, cancellationToken);
            if (Interlocked.Increment(ref _lookupCount) == 1)
            {
                await barrier.ArriveAsync();
            }
            return result;
        }
        public Task<ShortLink?> FindByCodeAsync(string code, CancellationToken cancellationToken) => inner.FindByCodeAsync(code, cancellationToken);
        public Task<LinkAnalytics?> GetAnalyticsAsync(string code, CancellationToken cancellationToken) => inner.GetAnalyticsAsync(code, cancellationToken);
        public Task<LinkCreateStatus> TryCreateAsync(ShortLink link, CancellationToken cancellationToken) => inner.TryCreateAsync(link, cancellationToken);
        public Task<string?> IncrementClickCountAndGetOriginalUrlAsync(string code, CancellationToken cancellationToken) => inner.IncrementClickCountAndGetOriginalUrlAsync(code, cancellationToken);
    }

    private sealed class InitialLookupBarrier
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public Task BothInitialLookups => _both.Task;
        public void Release() => _release.TrySetResult();
        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _both.TrySetResult();
            }
            await _release.Task;
        }
    }
}