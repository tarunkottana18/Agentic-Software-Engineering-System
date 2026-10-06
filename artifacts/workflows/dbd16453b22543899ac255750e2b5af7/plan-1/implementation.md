# Implementation proposal

- Workflow: `dbd16453-b225-4389-9ac2-55750e2b5af7`
- Plan version: `1`
- Category: `implementation`

## Output

Brownfield Project Implementer agent changed the approved service and API test files. After workspace hash inventory is computed and the reported paths exactly match, a successful Brownfield implementation result is rejected if no verified path begins src/ or tests/. Added integration regression verifies HTTP 400 and that the stage remains incomplete for an empty diff. Existing positive manifest test remains; Greenfield behavior is unchanged.
