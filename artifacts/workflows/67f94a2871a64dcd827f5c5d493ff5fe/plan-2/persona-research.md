# Persona and pressure context

- Workflow: `67f94a28-71a6-4dcd-827f-5c5d493ff5fe`
- Plan version: `2`
- Category: `discovery`

## Output

Persona: local developer or operator checking whether the standalone API process is responding. Under time pressure they need a stable, low-cost endpoint suitable for a liveness probe and a clear distinction from dependency readiness.
Maintainer needs an integration test and route documentation to prevent the status contract drifting. No user study or external research is claimed.
Constraint: this endpoint must not query SQLite, reveal connection/configuration details, or claim downstream dependencies are healthy.
