# Product purpose and outcomes

- Workflow: `dbd16453-b225-4389-9ac2-55750e2b5af7`
- Plan version: `1`
- Category: `discovery`

## Output

Problem: the workflow currently accepts a successful Brownfield implementation result when the exact hash-verified workspace diff is empty. This allows a no-op to be recorded as an implementation.
Outcome: prevent completion of Brownfield implementation unless at least one actual source or test file changed under the tracked workspace roots and the reported changedFiles exactly matches that verified manifest.
Success: integration test submits a successful Brownfield implementation with no edits and confirms rejection; existing changed-file verification test confirms a real non-empty diff remains accepted.
Non-goals: Greenfield behavior, documentation-only change semantics, source rollback, new external-agent sandboxing, or trusting caller test output.
