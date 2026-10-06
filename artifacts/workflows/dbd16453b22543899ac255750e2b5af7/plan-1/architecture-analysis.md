# Architecture/design analysis

- Workflow: `dbd16453-b225-4389-9ac2-55750e2b5af7`
- Plan version: `1`
- Category: `design`

## Output

Minimal change in the existing .NET 10 layered API: preserve the current WorkflowStageResultRequest contract and workspace hash inventory. In EngineeringWorkflowService.SubmitStageResultAsync, after computing and verifying the implementation changed-file manifest, reject successful Brownfield implementation if the verified list is empty. Keep Greenfield semantics unchanged. Use WorkflowValidationException so API exception middleware maps to 400. No schema/API response changes.
