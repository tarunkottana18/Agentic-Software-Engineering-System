# Test strategy

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `testing`

## Output

Integration test with the existing WebApplicationFactory: GET /health/live returns HTTP 200, content type application/json, and JSON status exactly alive. Run the complete Greenfield solution test suite from its approved project root. Runtime smoke the route on the Development API and verify Swagger OpenAPI includes /health/live. No database migration is required.
