# URL Shortener API

Standalone .NET 10 URL-shortening REST API using ASP.NET Core, EF Core, and SQLite.

## Boundaries

- `UrlShortener.Core`: short-link entity and repository contract.
- `UrlShortener.Application`: validation and use cases; references Core.
- `UrlShortener.Infrastructure`: EF Core/SQLite repository, migrations, and cryptographic code generator.
- `UrlShortener.Api`: REST endpoints and composition root.
- `UrlShortener.Tests`: API and persistence behavior tests using isolated SQLite databases.

## API

- `GET /` returns API status and the available routes.
- `POST /api/v1/links` with `{ "originalUrl": "https://example.com" }` creates a mapping or returns an exact duplicate.
- `GET /{code}` increments the count atomically and redirects with 302.
- `GET /api/v1/links/{code}/analytics` returns `{ "code": "...", "clickCount": 0 }`.

In Development, open `http://localhost:5209/swagger` for interactive API testing. Swagger UI is not enabled in other environments.

Configure `UrlShortener:PublicBaseUrl` to the externally reachable absolute HTTP(S) base URL and `ConnectionStrings:DefaultConnection` to the SQLite database. Short URLs are never built from the incoming request host. The application intentionally does not migrate the database at startup.

## First run

From this project directory, install the .NET 10 SDK and EF Core CLI tool if they are not already installed, then apply the checked-in migration before starting the API:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.0
Push-Location .\src\UrlShortener.Api
dotnet ef database update --project ..\UrlShortener.Infrastructure\UrlShortener.Infrastructure.csproj --startup-project .\UrlShortener.Api.csproj
Pop-Location
dotnet run --project src/UrlShortener.Api
```

The initial migration creates the `Links` table. Without applying it, the API can start but link and analytics requests fail because the database schema is absent. The API listens on the address printed at startup (the default launch profile uses `http://localhost:5209`).

## Migrations

The initial migration and model snapshot are checked in. After installing the .NET 10 SDK and the EF Core CLI tool, future schema changes can be managed manually from this directory:

```powershell
dotnet ef migrations add <MigrationName> --project src/UrlShortener.Infrastructure --startup-project src/UrlShortener.Api
dotnet ef database update --project src/UrlShortener.Infrastructure --startup-project src/UrlShortener.Api
```

The CLI tool is not required to run the application or its tests.
