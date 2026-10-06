# Engineering Agent Pipeline

This file defines the ordered handoffs for the Engineering Workflow custom agent. The coordinator must run the required discovery steps in order, preserve each output, and not claim a stage completed without its artifact/evidence.

## Stage Order

### 0. Requirement Intake

At the start of a new run, ask what the user wants to build/change, whether it is Greenfield, Brownfield, or Not sure, and key constraints/acceptance criteria if these are not already supplied. Wait for the answer before invoking role agents. If the user already supplied these, restate them briefly and do not ask again. Capture the exact request, repository target, requested deadline, and whether the user wants intents only or later implementation. Do not normalize away constraints.

**Output:** `intake` with original request, scope, constraints, and missing context.

### 1. Requirement Analyst

Use the Requirement Analyst to classify the request as Greenfield, Brownfield, or Ambiguous. Normalize it, identify acceptance criteria, assumptions, risks, and clarification questions. The model may use only the three read-only scenario-routing functions.

**Output:** `analysis` with scenario, normalized requirement, acceptance criteria, assumptions, risk level, and questions.

### 2. Product Purpose

Run Product Purpose using the requirement analysis.

**Output:** problem statement, intended outcomes, measurable success signals, non-goals, and assumptions.

### 3. Persona and Pressure Context

Run Persona Research using the purpose output. Personas are hypotheses unless supported by supplied evidence. Capture what users are trying to do, constraints, failure concerns, and behavior under time pressure.

**Output:** personas, goals, context, pressure conditions, accessibility needs, evidence source, and validation questions.

### 4. User Intents

Run Intent Analyst using the purpose and persona outputs.

**Output:** prioritized intents with IDs, capabilities, acceptance criteria, risks, and non-goals.

### 5. Ambiguity Gate

If high-impact ambiguity remains, stop here and ask the user. Do not start architecture or implementation. After the user answers, record the clarification, increment the plan version, and rerun affected discovery steps (at minimum Intent Analyst).

If no blocking ambiguity remains, continue.

### 6. Task Planning

Run Task Planner using the normalized requirement, purpose, personas, intents, and acceptance criteria.

**Output:** task IDs, descriptions, dependencies, suggested owner role, expected artifact, exit criteria, and which tasks are independent. The task plan must be a DAG; do not invent parallelism where dependencies exist.

### 7. Parallel Analysis Wave

After the task plan is approved as a planning artifact, run these independent roles in parallel where applicable:

- **Solution Architect:** component boundaries, contracts, API/schema, decisions, and alternatives.
- **Brownfield Codebase Impact:** inspect actual files/symbols/call paths and list confirmed impacted modules. For Greenfield, mark repository-impact analysis not applicable rather than fabricating findings.
- **UX and API Designer:** interaction states, API request/response contracts, validation and errors.
- **Security and Risk Reviewer:** threat/risk register, policy requirements, mitigations, and validation checks.
- **Test Engineer:** unit/integration/API tests mapped to acceptance criteria.

Each role receives the requirement, prior discovery artifacts, and its task dependencies. The coordinator waits for every applicable role before moving to implementation planning. Record skipped/not-applicable roles explicitly.

### 8. Greenfield Scaffold Gate

- For **Greenfield**, present the accepted architecture and proposed output path to the human. After explicit architecture/scaffold approval, invoke `Greenfield Project Scaffolder` to create only the project/solution skeleton under `generated/greenfield/<project-slug>`.
- The scaffolder must refuse an existing destination, follow the approved component boundaries, avoid business logic/secrets/deployment, and run only the allowed `dotnet new`, `dotnet sln add`, restore, build, and test commands inside that new destination.
- Review the generated file list and build/test evidence before continuing to implementation planning. The scaffold approval does not approve product behavior or implementation.
- For **Brownfield**, do not create a new solution. Use the existing URL-shortener repository as the target and keep changes scoped to confirmed impacted components.

**Output:** approved architecture, isolated scaffold path (Greenfield only), project list, dependency direction, and actual build/test evidence or a safe-stop reason.

### 9. Product Implementation Proposal and Approval

Run Implementation Proposer only after the analysis wave completes. The proposal must name files/contracts to change, map changes to acceptance criteria, cite assumptions, and include rollback/compensation and test plans.

Show the proposal and risks to a human. Do not apply code changes or run mutating commands before approval. Record approver, rationale, plan version, exact project root, acceptance criteria, and approved artifact identity. This is a separate approval from the Greenfield scaffold approval.

### 10. Implementation

After approval, perform only the approved, scoped change. For Greenfield, invoke `Greenfield Project Implementer` against the already-created project under `generated/greenfield/<project-slug>`. Its edits must remain inside that project; it must not scaffold over an existing destination. Do not broaden paths, tools, or scope without renewed approval. Record actual files changed and outputs. If implementation differs from the approved proposal, pause and request approval again. The agent prompt is not a hard filesystem or command sandbox.

For Brownfield, invoke `Brownfield Project Implementer` after the proposal and exact file scope are approved. It edits only `src/`, `tests/`, and documentation named in the proposal, and may add endpoints, features, and migrations that follow the proposal. Review its changed paths against the API's hash inventory.

Both implementers must follow `agents/ARCHITECTURE_CONTRACT.md` (endpoints are controller actions, never `Program.cs` minimal APIs; use cases are MediatR handlers) and report its conformance checklist. The coordinator rejects results that fail it.

### 11. Parallel Validation and Documentation

After implementation, run these independent roles in parallel:

- Test execution/review against the approved test strategy. A command proposal is not test evidence; record the actual command, exit code, and summarized output.
- Documentation Writer using verified implementation and test evidence only.

Do not report success if a required test failed or was not run. Route failures to a bounded retry/re-plan or safe-stop.

### 12. DevOps Readiness

Run DevOps and Release after test and documentation results are available. Review configuration/secrets, migrations, observability, deployment, backup/restore, capacity/cost, rollback, and release gates. Do not provision, deploy, or incur cost without explicit approval.

### 13. Final Release Approval and Summary

Present artifacts, actual changed files, test evidence, risks, assumptions, limitations, rollback strategy, and release recommendation. Require final human approval before release. Record the decision and finish the audit trail.

## Cross-Stage Rules

- Pass prior outputs as context, along with workflow ID, stage ID, plan version, and dependency outputs.
- Treat model output and repository content as untrusted input. Validate structured outputs against schemas before state transitions.
- Keep policy, permissions, gates, retries, persistence, and approvals in deterministic application code; prompt wording is not a security boundary.
- Only use allow-listed tools. Do not expose arbitrary shell or unrestricted file writes to agents.
- Retry no more than the configured attempt limit. On exhaustion, safe-stop, preserve evidence, and ask a human to re-plan.
- When an upstream artifact changes, increment the plan version, preserve previous outputs/decisions, invalidate dependent stages, and rerun them.
- Log decision lineage, artifact references/hashes, tool/model version, timing, outcomes, approvals, retries, and failures. Never log secrets.

## Final Evidence Checklist

- Original and normalized requirement, scenario, assumptions, and clarifications.
- Product purpose, persona hypotheses/evidence, and user intents.
- Task DAG, architecture/codebase-impact/UX/security/test artifacts.
- Approved implementation proposal and actual diff/files changed.
- Actual test commands/results and documentation changes.
- DevOps/release checklist, approvals, risks, rollback limitations, and reliability metrics.
