# Architecture/design analysis

- Workflow: `58d44ded-9526-4c4e-9007-0c8e5a0f8681`
- Plan version: `1`
- Category: `design`

## Output

Architecture basis: user-approved on 2026-10-06; reviewed against generated/greenfield/url-shortener.

Topology: ASP.NET Core controller-only API -> MediatR request/handler use cases -> Core entities/repository contracts -> Infrastructure EF Core SQLite repository. Controllers own HTTP status, DTO, Location and redirect behavior. Program.cs composes dependencies and middleware only. This matches agents/ARCHITECTURE_CONTRACT.md.

Routes: POST /api/v1/links creates or reuses a mapping; GET /{code} resolves, increments atomically, and returns 302; GET /api/v1/links/{code}/analytics reads click count without changing it; GET / returns service metadata. Swagger UI is Development-only.

Persistence: ShortLink stores code, original URL and click count. EF Core migration creates the Links table. Startup does not migrate; first-run setup explicitly applies the migration. SQLite is suitable for this local POC, not asserted as a production-scale deployment choice.

Reliability/security: absolute HTTP(S) URL validation, cryptographic short codes, unique-code collision handling, server-configured public base URL, parameterized EF operations and atomic click updates. Known boundaries: no user identity, rate limiting, abuse moderation, custom-domain tenancy, distributed database, or production deployment proof.

Approval: user explicitly approved this architecture and use of the existing tested Greenfield project as the implementation baseline in this session. The workflow's distinct implementation-approval gate remains pending.
