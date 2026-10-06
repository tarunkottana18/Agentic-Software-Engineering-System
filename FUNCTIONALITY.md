# Functionality Guide

This guide describes what the current source code does and identifies behavior that is still missing. It is intentionally explicit about gaps so the documentation does not make the prototype sound more complete than it is.

## Normal URL shortening

1. A client sends `POST /api/url/shorten` with JSON containing `originalUrl`.
2. `UrlController` turns the request into a `ShortenUrlCommand` and sends it through MediatR.
3. `ShortenUrlHandler` asks `IUrlRepository` whether that original URL is already stored.
4. If it exists, the handler returns its current short code. Otherwise it generates a seven-character cryptographic code and adds a `UrlMapping`, retrying unique-code conflicts up to five times.
5. `UrlRepository` uses EF Core to save the mapping in SQLite.

The handler rejects non-absolute URLs and schemes other than HTTP/HTTPS. Invalid input becomes an HTTP 400 response. The response includes a short code and a URL made from `UrlShortenerSettings.BaseShortUrl`; the API maps `/{shortCode}` to the redirect action.

### Current gaps

- The public base URL is currently configured for localhost and must be changed for deployment.
- Collision behavior has unit-test coverage using a fake repository; SQLite collision translation still needs an integration test.
- Development uses `EnsureCreatedAsync`; production should use versioned EF Core migrations.

## Redirect and click count

1. A client requests `GET /{shortCode}` or `GET /api/url/redirect/{shortCode}`.
2. `UrlController` sends `GetOriginalUrlQuery` through MediatR.
3. `GetOriginalUrlHandler` finds the mapping. If it exists, it asks `IUrlRepository` to atomically increment the count with a single SQL update; it does not save a stale read-modify-write entity.
4. The controller returns an HTTP redirect to the original URL, or `404 Not Found` if the code does not exist.

This records a basic total click count. The repository performs a single atomic SQL update (`IncrementClickCountAsync`), so concurrent redirects do not lose clicks. `GET /api/url/{shortCode}/analytics` reads and returns the count without changing it. The write is still initiated inside a query handler; modelling redirect as an explicit command is a possible further cleanup.

## Natural-language agent endpoint

`POST /api/url/agent` passes a JSON string to `IAgenticOrchestrator`. The orchestrator applies a simple keyword/length filter and invokes a Semantic Kernel prompt. A plugin class contains methods that can dispatch URL commands and queries through MediatR.

### Current gaps

- Automatic function choice is configured for the imported plugin, but tool execution has not yet been runtime-tested with a valid model/API key.
- The API key in `appsettings.json` is a placeholder. Agent requests need valid model configuration.
- The keyword filter is not a reliable defense against prompt injection. It must not be treated as an authorization boundary.
- The URL-focused agent endpoint is separate from the SDLC workflow API below.

## Engineering workflow API

`POST /api/workflows` accepts a requirement. With valid model credentials, the Semantic Kernel analyzer loads `requirement-analyst.md` and uses `FunctionChoiceBehavior.Auto()` to choose one read-only scenario function: `SelectGreenfield`, `SelectBrownfield`, or `RequestClarification`. It validates the returned JSON before creating a workflow. Without credentials or valid model output, it records a deterministic local fallback classification instead.

After requirement analysis, role stages run sequentially through product purpose, persona/pressure context, user intents, and task decomposition. An ambiguous classification adds a human clarification gate and blocks downstream analysis; the caller submits the clarified requirement through the re-plan endpoint. Clear Greenfield/Brownfield requirements unlock architecture, codebase-impact, UX/API, security-risk, and test-strategy agents in parallel. Implementation is blocked until all branches complete and approval is granted. Test review and documentation then proceed in parallel, followed by DevOps readiness, release review, and final approval. The stage executor selects its Markdown role by stage ID and scenario type, passing prior stage outputs as context.

Operators or external agents can claim one ready stage with `/start` and submit success/failure, output, artifact references, and (for implementation) a `changedFiles` list to `/result`. Alternatively, `/execute-ready` drafts all ready non-approval stages concurrently through Semantic Kernel or a deterministic fallback, then records the batch before unlocking dependent work. Outputs and artifact references are size-limited; successful output is hashed in audit events. A failed stage gets at most three total attempts; exhaustion places the workflow in `SafeStopped` and prevents future dependent work.

Requirement changes and upstream-output changes create a new plan version and preserve the previous plan/stage snapshot. An upstream change resets dependent stages for review. Plan rollback restores the earlier requirement and analysis, removes artifacts for the rolled-back plan, and resets implementation and later stages so they must be reviewed again. Metrics include success rate, retries, plan rollback count, recovery time, and end-to-end latency.

Implementation/final approval and plan rollback require a configured `X-Approval-Token`. When `WorkflowGovernance:RequiredRole` is configured, callers must also send a matching `X-Approval-Role` header. That header is self-declared by the caller, so anyone holding the token can claim the role; it is not authenticated identity.

### Workflow artifacts and controlled delivery

- Successful stages write reviewable Markdown artifacts under `artifacts/workflows/<workflow-id>/plan-<version>/`. Relative paths in `WorkflowGovernance` settings resolve from the repository root (the nearest folder with a solution file), not the launch directory.
- For Greenfield workflows `codebase-impact` is completed automatically as "Not applicable" with a `StageNotApplicable` audit event, so it neither blocks implementation nor fabricates findings.
- At implementation approval, the API snapshots SHA-256 hashes of files under `src`, `tests`, and `generated/greenfield` (configurable with `WorkflowGovernance:WorkspaceRoot`). It excludes build output and rejects symbolic links in those tracked trees. On successful implementation `/result`, the API independently computes additions, modifications, and deletions, and requires the normalized workspace-relative `changedFiles` list to match exactly. Actual paths and before/after hashes are persisted in workflow state; an audit event records the verified manifest digest. Greenfield changes must be under `generated/greenfield/`. This verifies final file state, not the actor, edits reverted before submission, or supplied test-output authenticity.
- The `Greenfield Project Scaffolder` agent (not the API) creates a new solution under `generated/greenfield/<project-slug>`. It submits source paths in `changedFiles`; `artifactReferences` remain for report links. Build/test output is recorded as evidence but is not verified unless trusted validation is enabled.
- After the separate implementation proposal is approved, the `Greenfield Project Implementer` agent can implement the approved product behavior inside that existing generated project and run its tests. The coordinator remains read-only. Its path and command constraints are prompt-level instructions, not a hard sandbox.
- Trusted validation can run fixed `dotnet build --no-restore` and `dotnet test --no-restore` commands when `WorkflowGovernance:AllowTrustedCommands` is explicitly enabled. Arbitrary model-supplied commands are not accepted.
- Plan rollback removes artifacts for the rolled-back plan version. It does not reverse Brownfield source changes because the workflow does not apply arbitrary Brownfield patches.

### Workflow limitations

- Stage proposals can be drafted by Semantic Kernel or a local fallback. Source edits are made by an approved editor agent, not the workflow API; verification covers only final files under the configured source, test, and Greenfield roots.
- Greenfield product implementation is delegated to a dedicated role after proposal approval; Brownfield changes (new endpoints, features, migrations) are delegated to the `Brownfield Project Implementer` agent, scoped to `src/` and `tests/`. Both follow `agents/ARCHITECTURE_CONTRACT.md` (controller endpoints, MediatR use cases). The generated Greenfield project is present and its 19 tests pass; workflow `452fd55b-761b-4914-a866-4fd1627ce715` has only completed requirement analysis. Its [partial evidence note](docs/scenario-evidence/greenfield-partial-live-classification.md) and [JSON summary](docs/scenario-evidence/greenfield-partial-live-classification.json) are not a full workflow export. The Brownfield implementer has not yet completed an end-to-end workflow run. These agent rules are prompt-level, not a sandbox.
- The workflow analyzer Kernel exposes only the scenario-routing plugin. URL mutation tools are attached separately to the URL-agent request path. Workflow state transitions and policy remain deterministic Application code.
- The fallback analyzer is heuristic. Live Semantic Kernel requirement analysis was verified once with Ollama Cloud `gemma4:31b`; live execution of the later workflow stages is still unverified. Configure credentials through environment variables or user secrets.
- Plan rollback removes workflow-owned artifacts, but cannot reverse external changes or Brownfield source changes that this prototype does not apply.

## What is not implemented yet

- Real authentication and per-user roles (the token and role header are shared and caller-declared).
- Live model validation of stages beyond requirement analysis; the retained scenario exports used fallback, while one Greenfield requirement-analysis call used Ollama Cloud `gemma4:31b`.
- End-to-end Brownfield implementation validation and source-level rollback. An approved Brownfield Project Implementer is available, but has not yet been exercised in a complete workflow.
- Production-scale throughput/soak testing and separate-process deployment testing. Automated smoke coverage runs two independent API hosts against shared SQLite, races implementation approval, and sends 80 concurrent reads.

Recorded scenario runs are in [docs/scenario-evidence](docs/scenario-evidence).

See [PROJECT_PLAN.md](PROJECT_PLAN.md) for the recommended build order and [README.md](README.md) for project entry points and commands.
