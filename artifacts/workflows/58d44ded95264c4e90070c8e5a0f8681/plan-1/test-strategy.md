# Test strategy

- Workflow: `58d44ded-9526-4c4e-9007-0c8e5a0f8681`
- Plan version: `1`
- Category: `testing`

## Output

Risk-based validation plan, based on repository tests and API contracts:

Greenfield solution: run dotnet test generated/greenfield/url-shortener/UrlShortener.slnx. Cover create, duplicate reuse, invalid URL, unknown code, redirect status/Location, click count, migration and persistence behavior. Run Swagger UI and OpenAPI HTTP checks in Development.

Root solution: run dotnet test UrlShortenerSystem.sln. Cover handlers and API; atomic click behavior under concurrent/stale-reader access; workflow classification, dependency/readiness gates, approval-token rejection, retries/safe-stop, replan/rollback, artifact and workspace-verification rules; persistence/restart tests where configured.

Runtime smoke: apply Greenfield migration; POST a unique valid URL; query analytics (0); request returned short URL without following automatically and assert 302/Location; query analytics again (1). Validate GET /swagger is 200 HTML and /swagger/v1/swagger.json declares the expected routes.

Evidence rule: test-execution output must state the command, exit code and actual pass/fail/skip totals. Existing prior runs are background context only; rerun commands for this workflow. Production load/soak and separate-process deployment are outside this POC validation.
