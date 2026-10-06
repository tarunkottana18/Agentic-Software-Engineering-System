# User intents and capabilities

- Workflow: `dbd16453-b225-4389-9ac2-55750e2b5af7`
- Plan version: `1`
- Category: `discovery`

## Output

Intent: make Brownfield implementation success evidence require actual verified changes.
Acceptance: after implementation approval captures a source baseline, a successful implementation result with zero hash-detected changes is rejected with a validation error and does not mark the stage complete; a non-empty changed source/test set is still accepted only when reported paths exactly equal hash-detected paths. Greenfield behavior is unchanged.
