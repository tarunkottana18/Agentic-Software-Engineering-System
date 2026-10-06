using UrlShortener.Application.Common;
using UrlShortener.Application.Dtos;
using UrlShortener.Application.Features.Urls.Commands;
using UrlShortener.Core.Entities;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Interfaces;
using Xunit;

namespace UrlShortener.Tests;

public class ShortenUrlHandlerTests
{
    [Fact]
    public async Task Handle_ValidUrl_CreatesMappingAndRootShortUrl()
    {
        var repository = new InMemoryUrlRepository();
        var generator = new SequenceShortCodeGenerator("abc1234");
        var handler = CreateHandler(repository, generator);

        var response = await handler.Handle(new ShortenUrlCommand("https://example.com/path"), CancellationToken.None);

        Assert.Equal("abc1234", response.ShortCode);
        Assert.Equal("http://localhost:5000/abc1234", response.ShortUrl);
        Assert.Single(repository.Mappings);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    public async Task Handle_InvalidUrl_ThrowsAndDoesNotPersist(string originalUrl)
    {
        var repository = new InMemoryUrlRepository();
        var handler = CreateHandler(repository, new SequenceShortCodeGenerator("unused"));

        await Assert.ThrowsAsync<InvalidUrlException>(() =>
            handler.Handle(new ShortenUrlCommand(originalUrl), CancellationToken.None));

        Assert.Empty(repository.Mappings);
    }

    [Fact]
    public async Task Handle_ExistingUrl_ReturnsExistingCodeWithoutGeneratingAnother()
    {
        var repository = new InMemoryUrlRepository();
        var existing = new UrlMapping
        {
            Id = Guid.NewGuid(),
            OriginalUrl = "https://example.com",
            ShortCode = "saved01",
            CreatedAt = DateTime.UtcNow
        };
        await repository.AddAsync(existing);
        var generator = new SequenceShortCodeGenerator();
        var handler = CreateHandler(repository, generator);

        var response = await handler.Handle(new ShortenUrlCommand(existing.OriginalUrl), CancellationToken.None);

        Assert.Equal(existing.ShortCode, response.ShortCode);
        Assert.Equal("http://localhost:5000/saved01", response.ShortUrl);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task Handle_CodeCollision_RetriesWithNewCode()
    {
        var repository = new InMemoryUrlRepository("taken01");
        var generator = new SequenceShortCodeGenerator("taken01", "fresh01");
        var handler = CreateHandler(repository, generator);

        var response = await handler.Handle(new ShortenUrlCommand("https://example.com"), CancellationToken.None);

        Assert.Equal("fresh01", response.ShortCode);
        Assert.Equal(2, repository.AddAttempts);
    }

    [Fact]
    public async Task Handle_RepeatedCollisions_StopsAfterFiveAttempts()
    {
        var repository = new InMemoryUrlRepository("taken01");
        var generator = new SequenceShortCodeGenerator("taken01", "taken01", "taken01", "taken01", "taken01", "unused");
        var handler = CreateHandler(repository, generator);

        await Assert.ThrowsAsync<ShortCodeGenerationException>(() =>
            handler.Handle(new ShortenUrlCommand("https://example.com"), CancellationToken.None));

        Assert.Equal(5, repository.AddAttempts);
        Assert.Equal(5, generator.Calls);
    }

    private static ShortenUrlHandler CreateHandler(IUrlRepository repository, IShortCodeGenerator generator)
    {
        return new ShortenUrlHandler(
            repository,
            new UrlShortenerSettings { BaseShortUrl = "http://localhost:5000/" },
            generator);
    }

    private sealed class SequenceShortCodeGenerator(params string[] codes) : IShortCodeGenerator
    {
        public int Calls { get; private set; }

        public string Generate()
        {
            var index = Calls++;
            return index < codes.Length ? codes[index] : "fallback1";
        }
    }

    private sealed class InMemoryUrlRepository(params string[] reservedCodes) : IUrlRepository
    {
        private readonly Dictionary<string, UrlMapping> _mappings = new();
        private readonly HashSet<string> _reservedCodes = reservedCodes.ToHashSet(StringComparer.Ordinal);

        public IReadOnlyCollection<UrlMapping> Mappings => _mappings.Values;
        public int AddAttempts { get; private set; }

        public Task<UrlMapping?> GetByShortCodeAsync(string shortCode)
        {
            _mappings.TryGetValue(shortCode, out var mapping);
            return Task.FromResult(mapping);
        }

        public Task<UrlMapping?> GetByOriginalUrlAsync(string originalUrl)
        {
            var mapping = _mappings.Values.FirstOrDefault(item => item.OriginalUrl == originalUrl);
            return Task.FromResult(mapping);
        }

        public Task AddAsync(UrlMapping urlMapping)
        {
            AddAttempts++;
            if (_reservedCodes.Contains(urlMapping.ShortCode) || _mappings.ContainsKey(urlMapping.ShortCode))
            {
                throw new ShortCodeConflictException(urlMapping.ShortCode);
            }

            _mappings.Add(urlMapping.ShortCode, urlMapping);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(UrlMapping urlMapping)
        {
            _mappings[urlMapping.ShortCode] = urlMapping;
            return Task.CompletedTask;
        }

        public Task<bool> IncrementClickCountAsync(string shortCode)
        {
            if (!_mappings.TryGetValue(shortCode, out var mapping))
            {
                return Task.FromResult(false);
            }

            mapping.ClickCount++;
            return Task.FromResult(true);
        }
    }
}
