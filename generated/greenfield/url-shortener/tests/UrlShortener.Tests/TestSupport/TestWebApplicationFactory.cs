using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Application.Interfaces;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Tests.TestSupport;

internal sealed class TestWebApplicationFactory(string connectionString, IShortCodeGenerator? shortCodeGenerator = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["UrlShortener:PublicBaseUrl"] = "https://short.example/base"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LinkDbContext>>();
            services.AddDbContext<LinkDbContext>(options => options.UseSqlite(connectionString));
            if (shortCodeGenerator is not null)
            {
                services.RemoveAll<IShortCodeGenerator>();
                services.AddSingleton(shortCodeGenerator);
            }
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LinkDbContext>();
        var actualConnectionString = context.Database.GetConnectionString();
        if (!string.Equals(actualConnectionString, connectionString, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The test host did not use its isolated SQLite database.");
        }

        await context.Database.MigrateAsync();
    }
}