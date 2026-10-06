# Task decomposition

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `planning`

## Output

1. Add HealthController under generated/greenfield/url-shortener/src/UrlShortener.Api/Controllers with GET /health/live and a minimal JSON alive response. Keep endpoint in controller architecture; no MediatR or database call is needed for this constant liveness response.
2. Add an API integration test in tests/UrlShortener.Tests/ApiTests.cs for 200, JSON content type and status=alive.
3. Update generated/greenfield/url-shortener/README.md with route and explicit liveness-not-readiness semantics.
4. Run Greenfield solution tests and smoke-test the route plus Swagger; record actual output.
Exact approved files: HealthController.cs (new), ApiTests.cs, README.md. User approved this scope in the current conversation.
