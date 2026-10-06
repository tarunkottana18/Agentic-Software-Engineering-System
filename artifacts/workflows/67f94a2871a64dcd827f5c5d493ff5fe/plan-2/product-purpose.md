# Product purpose and outcomes

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `discovery`

## Output

Problem: operators and monitors need to distinguish whether the API process can answer requests from whether its database or external dependencies are ready. The project currently exposes a root information route, but no dedicated liveness route.
Outcome: a stable GET /health/live controller route returns a minimal 200 JSON liveness response while the process is serving HTTP. It does not disclose internals or query SQLite; it is not a readiness or dependency health probe.
Success: integration test verifies status 200, JSON content type and stable status field; Swagger exposes the route; setup docs explain its liveness-only meaning.
Non-goals: readiness checks, database connectivity, authentication, orchestration deployment configuration, and production monitoring.
Evidence source: approved user scope and inspection of the existing Greenfield controllers.
