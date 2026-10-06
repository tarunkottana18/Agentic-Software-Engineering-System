# User intents and capabilities

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `discovery`

## Output

Intent: determine whether the Greenfield API process is alive.
Acceptance: GET /health/live returns HTTP 200 and application/json containing status=alive while the API host is running; the action is controller-based and appears in Swagger; request does not query or depend on SQLite; test asserts the contract; README documents liveness-only semantics.
Non-goals: readiness, health of the database, diagnostics disclosure, auth, deployment orchestration.
