using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Application.Interfaces;
using UrlShortener.Core.Entities;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Tests.TestSupport;

namespace UrlShortener.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task HealthLiveReturnsMinimalAliveResponseWithoutDatabaseAccess()
    {
        using var factory = new TestWebApplicationFactory("Data Source=:memory:");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { "status" }, document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("alive", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RootReturnsServiceInformationAndAvailableEndpoints()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();

        var response = await client.GetAsync("/");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("URL Shortener API", document.RootElement.GetProperty("service").GetString());
        Assert.Equal("ready", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("endpoints").GetArrayLength());
    }

    [Fact]
    public async Task PostCreatesAndReusesExactMappingWithConfiguredBaseUrl()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await factory.InitializeDatabaseAsync();

        var originalUrl = "HTTP://Example.COM/a%2fb?keep=Case";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/links")
        {
            Content = JsonContent.Create(new { originalUrl })
        };
        request.Headers.Host = "attacker.invalid";
        var created = await client.SendAsync(request);
        var createdJson = await created.Content.ReadAsStringAsync();
        using var createdDocument = JsonDocument.Parse(createdJson);
        var createdBody = JsonSerializer.Deserialize<LinkBody>(createdJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(new[] { "code", "shortUrl" }, createdDocument.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.NotNull(createdBody);
        Assert.Matches("^[0-9A-Za-z]{7}$", createdBody.Code);
        Assert.Equal($"https://short.example/base/{createdBody.Code}", createdBody.ShortUrl);
        Assert.Equal(createdBody.ShortUrl, created.Headers.Location?.ToString());

        var repeated = await client.PostAsJsonAsync("/api/v1/links", new { originalUrl });
        var repeatedBody = await repeated.Content.ReadFromJsonAsync<LinkBody>();
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(createdBody, repeatedBody);
        Assert.Equal(createdBody.ShortUrl, repeated.Headers.Location?.ToString());

        var redirect = await client.GetAsync($"/{createdBody.Code}");
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal(originalUrl, redirect.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ConcurrentIdenticalPostsCreateOneMappingAndReturnCreatedAndReusedResponses()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        const string originalUrl = "https://concurrent-post.test/path";
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = Enumerable.Range(0, 2).Select(async _ =>
        {
            await start.Task;
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/links")
            {
                Content = JsonContent.Create(new { originalUrl })
            };
            return await client.SendAsync(request);
        }).ToArray();

        start.SetResult(true);
        var responses = await Task.WhenAll(requests);
        var createdResponse = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var reusedResponse = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var createdBody = await createdResponse.Content.ReadFromJsonAsync<LinkBody>();
        var reusedBody = await reusedResponse.Content.ReadFromJsonAsync<LinkBody>();

        Assert.NotNull(createdBody);
        Assert.Equal(createdBody, reusedBody);
        Assert.Equal(createdBody.ShortUrl, createdResponse.Headers.Location?.ToString());
        Assert.Equal(createdBody.ShortUrl, reusedResponse.Headers.Location?.ToString());

        await using var context = await database.CreateContextAsync();
        var persisted = Assert.Single(await context.Links.AsNoTracking().ToListAsync());
        Assert.Equal(createdBody.Code, persisted.Code);
        Assert.Equal(originalUrl, persisted.OriginalUrl);
    }

    [Fact]
    public async Task PostReturnsServiceUnavailableAfterFiveCodeCollisions()
    {
        await using var database = new SqliteTestDatabase();
        var generator = new RepeatingShortCodeGenerator("Clash01");
        using var factory = new TestWebApplicationFactory(database.ConnectionString, generator);
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();
        await using (var context = await database.CreateContextAsync())
        {
            context.Links.Add(new ShortLink("Clash01", "https://existing.test/mapping"));
            await context.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync("/api/v1/links", new { originalUrl = "https://new.test/mapping" });
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
        Assert.Equal("Unable to allocate a short code.", problem.Title);
        Assert.Equal(5, generator.Attempts);
    }

    [Theory]
    [InlineData("not a URL")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file")]
    [InlineData("")]
    public async Task PostRejectsInvalidUrlsAsProblemDetails(string originalUrl)
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/v1/links", new { originalUrl });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostRejectsUrlLongerThan2048Characters()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        await factory.InitializeDatabaseAsync();

        var response = await client.PostAsJsonAsync("/api/v1/links", new { originalUrl = $"https://x.test/{new string('a', 2037)}" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownRedirectAndAnalyticsReturnNotFound()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await factory.InitializeDatabaseAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Missing1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/links/Missing1/analytics")).StatusCode);
    }

    [Fact]
    public async Task AnalyticsHasExactShapeAndDoesNotIncrementCount()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await factory.InitializeDatabaseAsync();
        var post = await client.PostAsJsonAsync("/api/v1/links", new { originalUrl = "https://analytics.test/path" });
        var created = await post.Content.ReadFromJsonAsync<LinkBody>();
        Assert.NotNull(created);

        var firstRedirect = await client.GetAsync($"/{created.Code}");
        var secondRedirect = await client.GetAsync($"/{created.Code}");
        Assert.Equal(HttpStatusCode.Found, firstRedirect.StatusCode);
        Assert.Equal(HttpStatusCode.Found, secondRedirect.StatusCode);

        var analytics = await client.GetAsync($"/api/v1/links/{created!.Code}/analytics");
        var json = await analytics.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(HttpStatusCode.OK, analytics.StatusCode);
        Assert.Equal(new[] { "code", "clickCount" }, document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(created.Code, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("clickCount").GetInt32());

        var repeatedAnalytics = await client.GetFromJsonAsync<AnalyticsBody>($"/api/v1/links/{created.Code}/analytics");
        var finalAnalytics = await client.GetFromJsonAsync<AnalyticsBody>($"/api/v1/links/{created.Code}/analytics");
        Assert.Equal(2, repeatedAnalytics!.ClickCount);
        Assert.Equal(2, finalAnalytics!.ClickCount);
    }

    [Fact]
    public async Task RedirectAndAnalyticsResolveCodesWithExactCase()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await factory.InitializeDatabaseAsync();
        await using (var context = await database.CreateContextAsync())
        {
            context.Links.AddRange(
                new ShortLink("AbC1234", "https://case.test/upper"),
                new ShortLink("abc1234", "https://case.test/lower"));
            await context.SaveChangesAsync();
        }

        var upperRedirect = await client.GetAsync("/AbC1234");
        var lowerRedirect = await client.GetAsync("/abc1234");
        Assert.Equal(HttpStatusCode.Found, upperRedirect.StatusCode);
        Assert.Equal("https://case.test/upper", upperRedirect.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Found, lowerRedirect.StatusCode);
        Assert.Equal("https://case.test/lower", lowerRedirect.Headers.Location?.OriginalString);

        var upperAnalytics = await client.GetFromJsonAsync<AnalyticsBody>("/api/v1/links/AbC1234/analytics");
        var lowerAnalytics = await client.GetFromJsonAsync<AnalyticsBody>("/api/v1/links/abc1234/analytics");
        Assert.Equal("AbC1234", upperAnalytics!.Code);
        Assert.Equal("abc1234", lowerAnalytics!.Code);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/aBc1234")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/links/aBc1234/analytics")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRedirectsIncrementCountWithoutLostUpdates()
    {
        await using var database = new SqliteTestDatabase();
        using var factory = new TestWebApplicationFactory(database.ConnectionString);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await factory.InitializeDatabaseAsync();
        var post = await client.PostAsJsonAsync("/api/v1/links", new { originalUrl = "https://clicks.test/path" });
        var created = await post.Content.ReadFromJsonAsync<LinkBody>();
        const int requestCount = 48;

        var redirects = await Task.WhenAll(Enumerable.Range(0, requestCount).Select(_ => client.GetAsync($"/{created!.Code}")));
        Assert.All(redirects, response => Assert.Equal(HttpStatusCode.Found, response.StatusCode));

        var analytics = await client.GetFromJsonAsync<AnalyticsBody>($"/api/v1/links/{created!.Code}/analytics");
        Assert.Equal(requestCount, analytics!.ClickCount);
    }

    private sealed record LinkBody(string Code, string ShortUrl);
    private sealed record AnalyticsBody(string Code, int ClickCount);

    private sealed class RepeatingShortCodeGenerator(string code) : IShortCodeGenerator
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public string Generate()
        {
            Interlocked.Increment(ref _attempts);
            return code;
        }
    }
}