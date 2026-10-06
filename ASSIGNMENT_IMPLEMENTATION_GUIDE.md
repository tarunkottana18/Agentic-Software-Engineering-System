# Assignment Implementation Guide

## Goal in Simple Terms

Deliver two connected things:

1. A URL-shortener API that can create links, redirect visitors, and report click analytics.
2. A controlled engineering workflow that takes a software requirement through planning, design, implementation, tests, documentation, and release review.

The URL shortener is the sample product. The workflow that safely coordinates engineering work is the main differentiator.

## Current Project Status

### Built or partly built

- .NET 10 layered API projects and solution.
- Shorten and redirect endpoints, SQLite persistence, and Swagger.
- Click analytics read endpoint; redirects increment the click count.
- HTTP/HTTPS validation, cryptographic short-code generation, bounded collision retries.
- URL handler and click-count tests, HTTP integration tests, workflow API tests, and migration, restart, workspace-verification and shared-database concurrency tests.
- Persisted workflow DAG with parallel-ready branches, dependency gates, approval-token checks, bounded retries/safe-stop, plan rollback/re-planning, audit events, and metrics.
- Semantic Kernel requirement analyzer uses automatic function choice among Greenfield, Brownfield, and clarification tools, with a deterministic fallback. A live Ollama Cloud `gemma4:31b` requirement-analysis call was verified and classified Greenfield; later workflow stages have not been live-validated.
- Embedded Markdown role prompts cover product purpose, persona/pressure, intents, task planning, Greenfield/Brownfield architecture, UX/API, security risk, test strategy/review, implementation proposal, documentation, DevOps, release, and clarification.
- `execute-ready` generates ready-stage proposals in parallel using Semantic Kernel or a deterministic fallback; it does not edit code. It runs build/test only when the opt-in trusted validator is enabled.
- Documentation for architecture, functionality, setup, and known gaps.

### Still needed

- Real authentication and per-user roles; approval currently uses a shared token and a caller-declared role header.
- Live Semantic Kernel validation of workflow stages beyond requirement analysis; the retained Brownfield and Ambiguous exports used local fallback, while one new Greenfield requirement-analysis call used Ollama Cloud `gemma4:31b`.
- Source-level rollback and end-to-end use of the separate Brownfield implementer agent remain unproven. The atomic click-count Brownfield workflow itself completed with implementation, tests, and final approval; its change was not applied by the separate agent path.
- Production-scale load/soak and separate-process deployment testing.

Implemented since the first version of this guide: EF migrations, Markdown stage artifacts, plan-artifact rollback, an opt-in fixed build/test validator, Greenfield path enforcement, workspace hash verification for implementation files, and automated migration, restart-persistence, stale-revision, two-host SQLite race, and concurrent-read smoke tests. Recorded scenario runs are in `docs/scenario-evidence/`.

## Implementation Phases

### Phase 1: Finish the URL-Shortener Product

1. Keep `POST /api/url/shorten` and both redirect routes working with the returned short URL.
2. Keep analytics reads separate from state-changing click tracking, or model redirect as an explicit resolve-and-track command.
3. Extend API/database integration tests using temporary SQLite. Duplicate handling, missing codes, and restart persistence have coverage; SQLite collision translation still needs an integration test.
4. Keep database schema setup reproducible. `EnsureCreated` is acceptable for this development prototype; use EF Core migrations for deployment scenarios.

**Completion check:** tests prove the HTTP-to-SQLite flow, not only handler behavior. Basic shorten/redirect/click analytics and API-host restart persistence are covered; SQLite collision translation and broader failure cases remain.

### Phase 2: Model a Stateful Engineering Workflow (Implemented)

Create Application-layer workflow concepts independent of Semantic Kernel:

- `Workflow`: ID, submitted requirement, normalized requirement, current status, plan version, timestamps, and final result.
- `WorkflowStage`: stage ID/type, status, dependencies, input/output references, attempts, start/end times, and failure reason.
- `WorkflowEvent`: append-only record of decisions, stage transitions, approvals, tool executions, retries, and validation outcomes.
- `Approval`: approver, decision, reason, timestamp, and the exact plan/artifact version approved.

Persist workflow state so a workflow can be inspected and resumed after the API process restarts. Keep model prompts, credentials, and sensitive input out of ordinary logs; record safe summaries or hashes where appropriate.

**Current evidence:** workflow state, plan versions, approvals, and events are persisted in SQLite; a fresh database accepts the checked-in EF migration, and an API-host restart test reloads and updates a workflow. Development/testing use `EnsureCreatedAsync`; other environments run migrations.

### Phase 3: Build the Dependency Graph and Coordinate Stages (Implemented Prototype)

Use a directed acyclic graph (DAG) for the normal plan, with stage gates that validate inputs and outputs:

```text
Requirement intake
        |
Normalize and classify
        |
Decompose into tasks
        |
Architecture/change impact
       / \
Test strategy   Implementation plan
       \ /
      Approval gate
          |
   Implementation proposal
          |
     Run validation
       /       \
 Update docs   Release review
       \       /
      Final approval
          |
       Complete
```

- Run independent, read-only analysis branches in parallel, then synchronize before dependent stages.
- Use typed stage inputs and outputs; validate model output before it changes workflow state.
- Define entry/exit checks for every stage. A failed gate blocks dependent stages.
- Re-plan downstream work when an upstream requirement, assumption, or approval changes. Keep prior plan versions and explain why the plan changed.

Semantic Kernel may analyze requirements and draft stage proposals. Application code owns the graph, transitions, gates, retry policy, and metrics. The built-in workflow stage executor cannot edit source files or run arbitrary commands. For Greenfield, the coordinator requires separate human approvals for the architecture/scaffold and product implementation proposal, then delegates the skeleton and approved product behavior to separate agents under `generated/greenfield/<project-slug>`. For Brownfield, an approved Brownfield Project Implementer is available for scoped changes. Both implementer agents' path/command constraints are prompt-level guidance, not a hard sandbox. The workflow API verifies final file paths and hashes, but submitted test evidence is trusted unless the opt-in validator runs.

**Current evidence:** tests show sequential purpose/persona/intents/task planning, parallel architecture/impact/UX/security/test branches, dependency synchronization, ambiguous clarification blocking, implementation approval, retries/safe-stop, output-triggered re-planning, and plan rollback. Brownfield workflow `2ef88ef7-7f93-4169-9a73-d175e5c53f24` completed an atomic click-count change with 41 tests at that time and final approval; the request was narrowed after discovering click counting already existed, so this is related evidence, not the exact “add click analytics” scenario. The Greenfield project passes 19 tests. Workflow `58d44ded-9526-4c4e-9007-0c8e5a0f8681` completed all workflow stages as a POC, accepted the already-existing Greenfield project as its baseline, verified zero implementation source changes, and recorded 49 root tests plus 19 Greenfield tests. Its [evidence note](docs/scenario-evidence/greenfield-workflow-run.md) and [JSON summary](docs/scenario-evidence/greenfield-workflow-run.json) summarize the run; per-stage Markdown artifacts are under `artifacts/workflows/58d44ded95264c4e90070c8e5a0f8681/plan-1/`. The earlier live-model workflow (`452fd55b-761b-4914-a866-4fd1627ce715`) completed requirement analysis only. Brownfield and full Greenfield stage execution used deterministic fallback, not live Ollama. The API verifies final changed-file paths and hashes, but does not authenticate the editor or submitted test output.

### Phase 4: Human Control, Safety, and Reliability (Prototype Implemented)

- Require human approval before implementation is applied, files are changed, external publication happens, or release is approved.
- Make a proposal/diff first. Do not let the model run arbitrary shell commands or write arbitrary paths.
- Enforce path, file-type, tool, input-size, and secret-handling policies outside the prompt.
- Set a retry limit (for example, two retries after the first attempt), stage timeout, and a defined fallback. On exhausted retries, stop safely with evidence and a recovery suggestion.
- Make retried operations idempotent. Record a compensating action or rollback point before any approved mutation. If an action cannot be safely reversed, block it for explicit human decision.
- Record structured audit events with workflow/stage IDs, plan version, tool/model, decision, outcome, timing, approval, and error category. Redact secrets.
- Track and report: workflow success rate, retries per workflow, rollback count/frequency, end-to-end latency, and MTTR. Define MTTR as average time from a recorded failure to a recorded recovery.

A keyword prompt filter is not prompt-injection protection. Authorization must come from allow-listed tools, deterministic policy checks, and approval gates.

**Current evidence:** the shared approval token is required for approval/plan rollback; tests cover unauthorized approval, bounded retries/safe-stop, audit events, and plan rollback. Production identity, rollback of file changes, and command/tool policies remain future work.

### Phase 5: Demonstrate the Three Scenarios

Save a reviewable artifact for each scenario containing the initial request, normalized requirement, assumptions/questions, dependency graph, decisions, approvals, executed stages, test evidence, and final summary.

- **Greenfield:** “Create a URL-shortening API.” Show decomposition into API, domain, persistence, tests, and documentation tasks.
- **Brownfield:** “Add click analytics to existing short links.” Show the affected entity, handler, repository, API, tests, migration/data impact, and regression results.
- **Ambiguous:** “Make shared links safer.” Identify ambiguity (for example, expiry, access control, or abuse detection), ask a human or record approved assumptions, then revise the plan and its version.

Do not describe these scenarios as completed until the workflow has been run and the artifacts/tests exist.

**Completion check:** each scenario can be replayed from a clean database/test setup and its workflow history reviewed.

### Phase 6: Final Review and Release Readiness

1. Run restore, full solution build, unit tests, integration tests, and API smoke tests.
2. Review secrets, URL validation, policy enforcement, database changes, error handling, and package vulnerabilities.
3. Confirm that approval is recorded for release and that the final artifact links to source changes and test results.
4. Update README, architecture, functionality, and engineering summary to match verified behavior. Label remaining gaps and assumptions plainly.

**Completion check:** a reviewer can clone the repository, follow setup instructions, run tests, exercise the URL API, replay all three scenarios, and trace how the final outcome was approved.

## Requirement-to-Work Map

| Assignment requirement | Main implementation | Evidence to show |
|---|---|---|
| Requirement understanding | Intake, classification, clarification, normalized requirements | Input, questions/assumptions, normalized requirement |
| Task decomposition | Typed tasks with dependency edges | Saved plan graph and task status history |
| Brownfield reasoning | Change-impact stage that inspects existing modules/contracts | Affected-file/component report and regression tests |
| Workflow orchestration | Persisted DAG runner, stage gates, parallel branches, synchronization | Workflow state/events and orchestration tests |
| Engineering output | Bounded implementation proposals plus tests/docs/schema artifacts | Reviewable diff and generated artifacts |
| Validation and risk control | Deterministic validation, tests, policy checks, safe-stop | Test reports, gate decisions, recorded risks |
| Controlled autonomy | Allow-listed tools and version-specific human approvals | Approval records and a test proving unapproved work is blocked |
| Final summary | Generated/reviewed plan, artifacts, risks, assumptions, limitations | Final workflow result with links to evidence |

## Recommended Order

Complete Phase 1 first, then implement the smallest testable workflow through Phases 2–4, and use it to run the three scenarios in Phase 5. Do not spend time adding model complexity before workflow state, policy, and approval behavior are deterministic and testable.
