# Update documentation

- Workflow: `58d44ded-9526-4c4e-9007-0c8e5a0f8681`
- Plan version: `1`
- Category: `documentation`

## Output

Updated and validated README.md to point to the current Greenfield workflow run and distinguish it from the earlier live-classification-only run. Added docs/scenario-evidence/greenfield-workflow-run.md and .json with run ID, approvals, stage artifacts, actual test totals, HTTP smoke results, fallback-model source, and remaining limitations. Parsed the JSON evidence with PowerShell ConvertFrom-Json successfully. Documentation does not claim the existing code was authored during this run; implementation hash verification recorded zero source changes.
