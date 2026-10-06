# UX and API design

- Workflow: `dbd16453-b225-4389-9ac2-55750e2b5af7`
- Plan version: `1`
- Category: `ux`

## Output

No public API shape change. Existing result request remains succeeded/output/failureReason/artifactReferences/changedFiles. For Brownfield implementation success with zero actual workspace changes, return the existing ProblemDetails validation response (400); stage remains available for a corrected attempt. Successful non-empty change path and exact path/hash matching remain unchanged. Greenfield path behavior remains unchanged.
