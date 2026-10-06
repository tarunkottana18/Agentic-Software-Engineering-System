# UX and API design

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `ux`

## Output

Contract: GET /health/live. Healthy process response: 200 OK, application/json, {"status":"alive"}. No request body, auth, database access, or external detail. If the process cannot accept HTTP, normal connection failure indicates liveness failure. This route is separate from GET / service index and is not a readiness/dependency-health assertion.
