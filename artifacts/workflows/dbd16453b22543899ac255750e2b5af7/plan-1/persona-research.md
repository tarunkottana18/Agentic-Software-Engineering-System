# Persona and pressure context

- Workflow: `dbd16453-b225-4389-9ac2-55750e2b5af7`
- Plan version: `1`
- Category: `discovery`

## Output

Primary user: reviewer who needs the workflow to distinguish a genuine Brownfield change from an agent claiming success without changing tracked implementation/test files.
Developer under pressure: may accidentally submit success before saving code; should receive actionable validation failure rather than a false green stage.
Maintainer: relies on a persisted diff inventory to audit the change. No external user study is claimed.
