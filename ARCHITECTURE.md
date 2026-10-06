# Architecture Overview

## Simple view

The API receives an HTTP request, MediatR sends it to an application handler, and the handler uses a repository interface to read or write the URL mapping. Infrastructure implements that repository with EF Core and SQLite.

`API -> Application/MediatR -> Domain contract -> Infrastructure/SQLite`

Semantic Kernel adapters live in the separate `UrlShortener.Agents` project. It embeds the repository-level `agents/prompts/*.md` role files, routes scenarios with `FunctionChoiceBehavior.Auto()`, and loads a role prompt for each stage. The persisted SDLC workflow state machine is in Application; Infrastructure stores its state in SQLite and writes workflow artifacts. Discovery runs sequentially, analysis branches run in parallel, and DevOps/release stages wait for validation and documentation evidence. The VS Code workflow coordinator remains read-only. After separate human approvals, Greenfield Project Scaffolder creates the skeleton and Greenfield Project Implementer adds approved product behavior within that generated project. Their path and command constraints are prompt-level instructions, not a hard sandbox. Trusted workflow API build/test execution is fixed to approved `dotnet` commands and disabled unless explicitly enabled.

## Project responsibilities

- **Core (Domain):** `UrlMapping` and `IUrlRepository`. This project should remain independent of web, database, and AI frameworks.
- **Application:** URL use cases plus workflow dependencies, stage gates, approval state, retry policy, audit events, plan versioning, and reliability metrics. It depends on repository/analyzer abstractions.
- **Infrastructure:** `UrlDbContext`, repositories, short-code strategy, EF migrations, Markdown artifact storage, and the allow-listed trusted validation executor. It implements persistence contracts using EF Core/SQLite.
- **Agents:** prompt catalog, embedded Markdown roles, Semantic Kernel function routing, requirement analyzer, and stage proposal executor. It implements Application-owned agent ports without moving AI SDK dependencies into Application.
- **API:** ASP.NET Core controllers, configuration, dependency registration, and HTTP responses.

## Dependency direction

Application and Domain should not depend on Infrastructure or Agents. Infrastructure and Agents implement contracts owned by Application/Core. The API is the composition root that registers both implementations at startup.

## Patterns currently used or intended

- **Mediator:** Controllers dispatch URL commands/queries through MediatR rather than calling handlers directly.
- **Repository:** Application handlers use `IUrlRepository`; EF Core details remain in Infrastructure.
- **Adapter:** Semantic Kernel is kept in the Agents project behind Application analyzer/executor contracts.
- **State machine/DAG:** workflow stage dependencies determine readiness; retries, gates, approvals, and re-planning are deterministic Application behavior.
- **Strategy:** `IShortCodeGenerator` makes code generation replaceable and testable.
- **Prompt assets:** root-level `agents/prompts/*.md` files define each stage’s mission, input, output, and safety limits; the Agents project embeds and resolves them by filename.

The workflow persists a versioned JSON state snapshot with indexed status and an optimistic concurrency revision. Approval and rollback require a configured shared token and, optionally, a role header. The role header is supplied by the caller, so it is a coarse extra gate and not authenticated identity; production should use real identities and roles.

## Current limitations

- Scenario function calling has a deterministic no-credential fallback. Live requirement analysis was runtime-verified once with Ollama Cloud `gemma4:31b`; later workflow stages remain unverified against a live model.
- Short-link and API redirect routes now map to the redirect action. The public base URL is still configured for local development.
- Development schema initialization uses `EnsureCreatedAsync`; production uses the checked-in EF migration.
- Automated tests cover URL behavior, workflow gates, retries/safe-stop, scenarios, re-planning, metrics, EF migrations on a blank database, restart persistence, workspace-change verification, and a shared-SQLite race across two API hosts with 80 concurrent reads. This is a concurrency smoke test, not a throughput or soak benchmark.
- `GetOriginalUrlHandler` still changes analytics during a query; consider making that state change explicit, though the increment itself is atomic.
- Proposal generation is automated/fallback-capable. Successful stage results write Markdown artifacts under `artifacts/workflows/<workflow-id>/plan-<version>/`; creating a project outside the workflow does not automatically submit a stage result or export a scenario JSON. A [partial Greenfield evidence note](docs/scenario-evidence/greenfield-partial-live-classification.md) and [JSON summary](docs/scenario-evidence/greenfield-partial-live-classification.json) are available; neither is a full workflow snapshot. Greenfield scaffolding and implementation are agent-executed after approval. After implementation approval, the API independently hashes additions, modifications, and deletions under `src`, `tests`, and `generated/greenfield`, then requires the agent's `changedFiles` list to match. This verifies final workspace state, not who made the changes or whether submitted test output is genuine. The current generated Greenfield project's tests pass, while its persisted workflow (`452fd55b-761b-4914-a866-4fd1627ce715`) has completed requirement analysis only.

For current endpoint behavior see [FUNCTIONALITY.md](FUNCTIONALITY.md). For implementation order and acceptance criteria see [PROJECT_PLAN.md](PROJECT_PLAN.md).
