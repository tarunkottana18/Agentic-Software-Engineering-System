# Final Engineering Summary: Agentic URL Shortener System

## 1. Executive Summary
This project is a URL-shortener prototype with a persisted, governed workflow DAG. It demonstrates requirement analysis, dependency gates, approval checkpoints, bounded retries/safe-stop, audit history, metrics, plan re-planning and rollback, and Markdown stage artifacts. Agents execute stages and submit evidence to the API; humans approve through a token-gated endpoint. A Greenfield URL-shortener project is retained at `generated/greenfield/url-shortener` and its 19 tests pass. A live Semantic Kernel call using Ollama Cloud `gemma4:31b` classified a new Greenfield requirement; workflow `452fd55b-761b-4914-a866-4fd1627ce715` is persisted, but only requirement analysis is complete. It is not a completed end-to-end Greenfield scenario and has no exported scenario JSON or stage Markdown artifacts. The root solution's 48-test suite also passes.

## 2. Engineering Rationale & Design Decisions

### Architecture: Clean Architecture + Mediator Pattern
I chose **Clean Architecture** to ensure a strict separation of concerns:
- **Domain Layer**: Pure business entities and repository interfaces.
- **Application Layer**: Implemented using the **Mediator Pattern (MediatR)**. This was a critical decision to decouple the "request" from the "execution," allowing the AI agent to trigger specific use-cases (Commands/Queries) without knowing the internal implementation.
- **Infrastructure Layer**: Used **SQLite** for portability and zero-config deployment, ensuring the evaluator can run the project immediately.

### Agentic Orchestration Model
The "Agentic" nature of the system is achieved through a **Plugin-based Architecture**:
- **The Analyzer**: Uses Semantic Kernel for requirement analysis when configured and a deterministic fallback otherwise.
- **The Adapter (Plugins)**: I implemented an Adapter pattern to bridge the AI's probabilistic output with the system's deterministic business logic. The AI doesn't "write code" at runtime; it "invokes governed functions."
- **Workflow Governance**: Application code, not model prompts, owns the persisted graph, stage gates, retries, safe-stop, approvals, re-planning, and metrics. Approval uses a shared token and caller-provided name, not production identity management.

### Security & Reliability (Guardrails)
- **Prompt Filtering**: `PromptSanitizer` applies a keyword check and input-length limit. This is not a reliable prompt-injection defense or authorization boundary.
- **URL Validation**: The shortening handler accepts only absolute HTTP/HTTPS URLs.
- **Persistence**: A unique index on `ShortCode` protects uniqueness; indexed database lookups are not generally constant-time.

## 3. Scenario Examples and Status

The retained Brownfield and Ambiguous histories used local-fallback planning and a shared approval token. Their submitted implementation evidence predates workspace hash verification. Greenfield evidence is partial: the generated project exists and a live-model workflow record is persisted, but the workflow has not been taken through the required design, approval, implementation, validation, documentation, and release stages.

### Scenario B: Brownfield (Enhancement)
**Requirement**: "Make click counting on the existing redirect atomic so concurrent redirects do not lose clicks, and add regression tests."
- **Codebase reasoning**: the impact analysis targeted `IUrlRepository`, `UrlRepository`, `GetOriginalUrlHandler` and the test fake. A first requirement ("add click analytics") found that click counting already existed, so the requirement was narrowed to the real gap: a read-modify-write race on `ClickCount`.
- **Change**: added `IncrementClickCountAsync` (single-statement SQL update) and used it in the redirect handler. A stale-reader regression test fails under the old pattern and passes now.
- **Result**: the implementation record reports 41 tests passed at the time of the change; later tests brought the suite to 48. The workflow completed and persistence across an API host restart is covered by automated tests. This historical workflow export predates workspace hash verification.
- **Cleanup**: `UrlShorteningService` duplicated the old read-modify-write pattern but was unused (not registered in DI, no references), so it was deleted.

### Scenario C: Ambiguous Requirement
**Requirement**: "Make shared links safer."
- **Defect found live**: the first run classified this as Greenfield with no questions. The heuristic now flags comparative quality words (safer, better, faster, and similar) and a regression test covers the exact wording.
- **Orchestration**: classified Ambiguous with one clarification question; discovery ran; the clarification gate blocked all downstream analysis.
- **Resolution**: a human answer submitted through re-plan created plan v2 (Brownfield, link expiry) with the v1 snapshot preserved. Plan rollback without the token returned 401; with it, plan v3 restored the original requirement and reset implementation and later stages.

## 4. Trade-offs & Limitations
- **Trade-off (SQLite vs SQL Server)**: Chose SQLite for developer experience (DX) and portability over the scalability of SQL Server.
- **Collision Mitigation**: Short codes use cryptographic randomness, a database uniqueness constraint, and up to five attempts. This reduces but does not mathematically eliminate collisions.
- **Limitation**: Rollback restores a prior workflow plan and removes that plan's Markdown artifacts. It does not reverse source changes; the workflow does not apply code itself, so source changes are reverted through version control.
- **Verification boundary**: The workflow does not edit source code. A coding agent makes changes after approval. The API snapshots hashes when implementation is approved, independently hashes final additions/modifications/deletions under `src`, `tests`, and `generated/greenfield`, and rejects a `changedFiles` list that differs from that inventory. Hashes and a manifest digest are persisted in workflow state/audit events. This verifies final workspace state, not actor identity, transient edits later reverted, or the authenticity of submitted test output unless the opt-in trusted validator is enabled.
- **Limitation (governance)**: Approval requires a shared token (`X-Approval-Token`) compared in constant time, plus an optional `X-Approval-Role` header via `WorkflowGovernance:RequiredRole`. The role header is declared by the caller, so anyone holding the token can claim the role. This is a coarse gate for a prototype and not authenticated identity; production would use a real identity provider and per-user roles.
- **Verified by automated tests**: EF migrations apply to a blank database, workflow state survives a host restart and can be approved afterwards, stale revisions are rejected, and two independent API hosts sharing an on-disk SQLite database resolve a concurrent approval race with one success and one conflict while serving 80 concurrent reads. This is a load/concurrency smoke test, not a production throughput, soak, or separate-process deployment benchmark.
- **Model key safety**: With `SemanticKernel:ApiKey` unset or left as the placeholder, the system uses its local fallback planner, so the repository runs and tests without any key. Live requirement analysis was verified once using Ollama Cloud `gemma4:31b`; subsequent workflow stages have not been validated against the live model. Supply a key only through environment variables or user secrets.
- **Evidence**: exported workflow histories cover Brownfield and Ambiguous in `docs/scenario-evidence/`. Greenfield has a persisted but incomplete workflow record (`452fd55b-761b-4914-a866-4fd1627ce715`), a generated project, and a [human-readable partial evidence note](docs/scenario-evidence/greenfield-partial-live-classification.md) with a [machine-readable summary](docs/scenario-evidence/greenfield-partial-live-classification.json); a full Greenfield workflow snapshot has not been exported. Successful stage results create Markdown files under `artifacts/workflows/<workflow-id>/plan-<version>/`; creating project files outside a successful workflow stage does not create those records. The partial summary and Greenfield tests are not a substitute for a completed scenario run. Existing stage artifacts include:
  - Brownfield: `2ef88ef77f9341699a73d175e5c53f24` (14 files, all stages).
  - Ambiguous: `6f0fa30de1494b9da8feadba76145da6` (4 files, discovery only). Plan v2 artifacts were deleted by the rollback, as designed; the v2 plan is preserved in the exported JSON under `previousPlans`.
  - `da669d9dc985464d8ab414e97aeb2a19`: the first Brownfield attempt ("add click analytics"), which stopped after analysis once click counting turned out to exist already; it is not one of the three scenarios.

## 5. Setup Instructions
1. Clone the repository and install the .NET 10 SDK.
2. Set an approval token for the session, for example in PowerShell: `$env:WorkflowGovernance__ApprovalToken='choose-a-token'`. No model key is needed; do not put one in `appsettings.json`.
3. Run `dotnet run --project src/UrlShortener.Api`.
4. Open the printed address plus `/swagger` (Development environment).
5. Run the tests with `dotnet test tests/UrlShortener.Tests/UrlShortener.Tests.csproj`.
