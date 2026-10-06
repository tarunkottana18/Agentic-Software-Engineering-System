# User intents and capabilities

- Workflow: `58d44ded-9526-4c4e-9007-0c8e5a0f8681`
- Plan version: `1`
- Category: `discovery`

## Output

Evidence basis: requirements mapped to the existing Greenfield project and its integration tests.

1. Create a short link. Acceptance: POST /api/v1/links accepts an absolute HTTP(S) originalUrl; returns code and shortUrl; exact duplicate mappings are reused; invalid URLs return a client error.
2. Redirect recipients. Acceptance: GET /{code} returns 302 to the stored destination; unknown codes return 404; click increments are atomic.
3. Inspect analytics. Acceptance: GET /api/v1/links/{code}/analytics returns code and clickCount; unknown codes return 404; reading analytics does not increment clicks.
4. Run and review locally. Acceptance: schema migration is reproducible; Development Swagger lists the routes; automated tests and runtime smoke checks provide attached evidence.
5. Govern changes. Acceptance: architecture and implementation approvals precede implementation-stage completion; test output is actual command output; final review records remaining risks rather than claiming production readiness.
