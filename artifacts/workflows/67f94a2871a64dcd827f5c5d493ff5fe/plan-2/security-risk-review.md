# Security and risk review

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `security`

## Output

Low-risk local liveness route. Return only a constant status; expose no versions, configuration, database state, environment names, or exception details. No authorization is added because the response discloses no sensitive data and is intended for liveness probes. It must not report readiness or dependency health. Production abuse controls remain outside this endpoint scope.
