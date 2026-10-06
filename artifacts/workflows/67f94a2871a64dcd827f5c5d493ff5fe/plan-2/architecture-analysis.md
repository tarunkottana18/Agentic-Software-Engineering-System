# Architecture/design analysis

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `design`

## Output

Approved architecture: extend the isolated Greenfield API project only. Add HealthController in UrlShortener.Api.Controllers with route GET /health/live. The controller returns a fixed minimal JSON response {"status":"alive"}; it does not depend on DI, database, or external services. This is liveness, not readiness.
Preserve the established ASP.NET Core controller-only architecture. Swagger automatically discovers the controller. No dependency, schema, configuration, or migration changes. Exact files: src/UrlShortener.Api/Controllers/HealthController.cs (new), tests/UrlShortener.Tests/ApiTests.cs, README.md.
