using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Application.Features.Urls.Queries;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Interfaces;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Repositories;
using Xunit;

namespace UrlShortener.Tests;

public sealed class ClickCountRegressionTests
{
    [Fact]
    public async Task IncrementClickCountAsync_DoesNotLoseClicksFromStaleReaders()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<UrlDbContext>().UseSqlite(connection).Options;
        await using (var setup = new UrlDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.UrlMappings.Add(new UrlMapping
            {
                Id = Guid.NewGuid(),
                OriginalUrl = "https://example.com",
                ShortCode = "abc1234"
            });
            await setup.SaveChangesAsync();
        }

        await using var first = new UrlDbContext(options);
        await using var second = new UrlDbContext(options);
        var firstRepository = new UrlRepository(first);
        var secondRepository = new UrlRepository(second);

        Assert.NotNull(await firstRepository.GetByShortCodeAsync("abc1234"));
        Assert.NotNull(await secondRepository.GetByShortCodeAsync("abc1234"));
        Assert.True(await firstRepository.IncrementClickCountAsync("abc1234"));
        Assert.True(await secondRepository.IncrementClickCountAsync("abc1234"));

        await using var verify = new UrlDbContext(options);
        Assert.Equal(2, (await verify.UrlMappings.SingleAsync()).ClickCount);
    }

    [Fact]
    public async Task IncrementClickCountAsync_UnknownCode_ReturnsFalse()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<UrlDbContext>().UseSqlite(connection).Options;
        await using var context = new UrlDbContext(options);
        await context.Database.EnsureCreatedAsync();

        Assert.False(await new UrlRepository(context).IncrementClickCountAsync("missing"));
    }

    [Fact]
    public async Task Redirect_IncrementsClickCountOnceThroughRepository()
    {
        var repository = new CountingRepository("abc1234", "https://example.com");
        var handler = new GetOriginalUrlHandler(repository);

        var response = await handler.Handle(new GetOriginalUrlQuery("abc1234"), CancellationToken.None);

        Assert.Equal("https://example.com", response?.OriginalUrl);
        Assert.Equal(1, repository.IncrementCalls);
        Assert.Equal(0, repository.UpdateCalls);
    }

    [Fact]
    public async Task Redirect_UnknownCode_DoesNotCountAClick()
    {
        var repository = new CountingRepository("abc1234", "https://example.com");
        var handler = new GetOriginalUrlHandler(repository);

        var response = await handler.Handle(new GetOriginalUrlQuery("missing"), CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(0, repository.IncrementCalls);
    }

    private sealed class CountingRepository(string shortCode, string originalUrl) : IUrlRepository
    {
        private readonly UrlMapping _mapping = new()
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            OriginalUrl = originalUrl
        };

        public int IncrementCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<UrlMapping?> GetByShortCodeAsync(string code) =>
            Task.FromResult<UrlMapping?>(code == _mapping.ShortCode ? _mapping : null);

        public Task<UrlMapping?> GetByOriginalUrlAsync(string url) => Task.FromResult<UrlMapping?>(null);

        public Task AddAsync(UrlMapping urlMapping) => Task.CompletedTask;

        public Task UpdateAsync(UrlMapping urlMapping)
        {
            UpdateCalls++;
            return Task.CompletedTask;
        }

        public Task<bool> IncrementClickCountAsync(string code)
        {
            IncrementCalls++;
            return Task.FromResult(true);
        }
    }
}
