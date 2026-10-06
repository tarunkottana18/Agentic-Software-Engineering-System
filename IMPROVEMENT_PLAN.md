# POC Improvement Plan

## Purpose

This document is the remaining-work roadmap for evolving the current URL-shortener prototype into a reviewable Agentic Software Engineering System. It is not the implementation status report; use `FUNCTIONALITY.md` and `ENGINEERING_SUMMARY.md` for current verified behavior. Treat roadmap items as incomplete until code and tests demonstrate them.

## Current-State Findings

- A solution and test project exist, with URL handler and HTTP tests plus workflow API tests. Migration, restart-persistence, workspace hash-verification, shared-database multi-host race, and concurrent-read smoke tests exist. Production-scale throughput/soak and separate-process deployment tests remain open.
- The shortener maps its returned `BaseShortUrl/{code}` through a root route as well as the API redirect route.
- The command handler validates HTTP/HTTPS URLs and uses an injected cryptographic code generator with five bounded attempts. SQLite conflict translation needs integration coverage.
- Development and testing startup create the schema with `EnsureCreatedAsync`; other environments apply the checked-in EF migration with `MigrateAsync`.
- Swagger and Semantic Kernel package references are declared. Requirement analysis uses Semantic Kernel when configured and a deterministic fallback otherwise; live model behavior still needs verification.
- A persisted workflow DAG now supports stage dependencies, parallel-ready analysis, token-gated approvals, bounded retries/safe-stop, audit events, metrics, plan rollback, and re-planning after requirement/upstream-output changes.
- Role-specific Markdown prompts are embedded in Infrastructure and selected for requirement analysis and each workflow stage.
- Stage outcomes are submitted by agents/operators and recorded by the API. The API does not edit source or run commands itself, except the opt-in fixed `dotnet build`/`dotnet test` validator. Authenticated user identity and source-level rollback are not implemented.
- The prompt keyword filter is not a dependable prompt-injection defense. Security must come from deterministic validation, least-privilege tools, policy checks, and approvals.
- Clicks are incremented atomically during URL resolution, and an analytics-read endpoint reports the total. Brownfield source files are independently hashed after implementation approval; test results remain unverified unless the trusted validator is enabled.

## Target Architecture

Keep the existing Clean Architecture boundaries and use the API's `Program.cs` as the composition root:

- **Domain (`UrlShortener.Core`)**: Entities, value objects, domain rules, and persistence-independent contracts. No dependency on ASP.NET Core, EF Core, or Semantic Kernel.
- **Application (`UrlShortener.Application`)**: Use cases, MediatR requests/handlers, validation, workflow contracts/state, and ports for external services. No dependency on Semantic Kernel or EF Core.
- **Infrastructure (`UrlShortener.Infrastructure`)**: EF Core/SQLite implementations, workflow persistence, and database integrations. Implements Application/Domain contracts.
- **Agents (`UrlShortener.Agents`)**: Semantic Kernel adapters, function-calling tools, and loader for versioned `agents/prompts/*.md` role instructions. Implements Application agent ports.
- **API (`UrlShortener.Api`)**: HTTP contracts, authentication/authorization policy, exception-to-ProblemDetails mapping, and composition-root registration. It should not contain business rules.
- **Tests (`tests`)**: Unit tests for domain/application, infrastructure integration tests using temporary SQLite databases, and API tests for HTTP contracts.

Dependency direction: `Api -> Application`, `Api -> Infrastructure` for composition only, `Infrastructure -> Application/Domain`, and `Application -> Domain`. Domain must not depend on outer layers.

## SOLID Rules to Apply

- **Single Responsibility**: Keep each handler focused on one use case. Keep request validation, URL/code generation, persistence, orchestration, and HTTP translation in their own components.
- **Open/Closed**: Make short-code generation and model/workflow stage execution replaceable behind small interfaces. Add behavior through implementations or workflow nodes instead of growing large conditional handlers.
- **Liskov Substitution**: Define repository and executor contracts by observable behavior, then run shared contract tests against each implementation. Do not let implementations silently change null, duplicate, or cancellation semantics.
- **Interface Segregation**: Keep interfaces use-case-focused. Split read/write repository contracts only when separate consumers need them; avoid a generic repository with unrelated operations.
- **Dependency Inversion**: Application handlers depend on domain/application interfaces. Keep Semantic Kernel and EF Core types out of Application contracts. Register implementations at the composition root through layer-specific registration extensions.

## Design Patterns and Where They Fit

- **Mediator / CQRS (MediatR)**: Keep HTTP controllers decoupled from use-case handlers. Keep read operations side-effect-free. Redirect tracking changes state, so model it explicitly as a command or a single atomic resolve-and-track command rather than hiding a write inside a query.
- **Repository**: Keep EF Core access behind the repository contract where it provides a useful boundary. EF Core `DbContext` already provides unit-of-work behavior; do not add a second generic unit-of-work abstraction without a demonstrated need.
- **Strategy**: Introduce `IShortCodeGenerator` so a secure random/Base62 implementation can be tested and changed independently. Retry collisions a bounded number of times and return a controlled failure after the bound.
- **Options**: Bind and validate `BaseShortUrl`, database, model, and timeout settings at startup. Do not hardcode localhost or commit API keys; use environment variables/user secrets for credentials.
- **Adapter**: Put Semantic Kernel behind Application-owned agent/model ports. The Agents adapter translates those ports into Semantic Kernel calls and tool invocations; business handlers should not reference Kernel APIs.
- **Workflow State Machine / DAG**: Represent SDLC stages as nodes with explicit dependencies, state transitions, gates, retry limits, and outcomes. This is the core orchestration pattern; a simple prompt that returns text is not a workflow engine.
- **Decorator / MediatR Pipeline Behaviors**: Apply cross-cutting concerns such as validation, timing, correlation IDs, and structured logging without duplicating them in each handler.

## Implementation Sequence

### 0. Establish a Reproducible Baseline

1. Install/use a supported .NET SDK and add a solution file at the repository root.
2. Declare every package used by source code in the owning project, including Semantic Kernel/OpenAI connector and Swagger packages, with compatible versions.
3. Add a test project and document exact root-level build/test commands.
4. Run restore, build, and tests; record the initial failures rather than assuming the project compiles.

**Exit gate:** clean package restore and build; no committed secrets; one reproducible test command.

### 1. Make the URL Shortener Correct End-to-End

1. Define and document the public route contract. Make generated short links point to a route that exists, preferably `GET /{shortCode}` for user-facing redirects.
2. Validate absolute `http`/`https` URLs before persistence and return a client validation response for invalid input.
3. Introduce a secure `IShortCodeGenerator`; enforce uniqueness with the database constraint and bounded collision retry.
4. Add EF Core migrations or a documented development-only database initialization path. Avoid silently relying on a missing schema.
5. Propagate cancellation tokens through handlers and repository operations.
6. Decide whether click tracking should use an atomic database update or an append-only click-event table. Document the privacy/data-retention trade-off.
7. Configure the public base URL and redirect behavior through validated options. Ensure local HTTP/HTTPS ports agree with launch configuration.

**Exit gate:** integration tests should additionally cover not-found behavior, SQLite collision translation, and persistence across restart. Current tests cover create, redirect, invalid input, analytics increment/read, duplicate reuse, and bounded retry using a fake repository.

### 2. Implement a Governed SDLC Workflow

Create an Application-owned workflow model independent of Semantic Kernel. A workflow instance should include an ID, input requirement, normalized requirement, plan, stage states, dependency edges, decisions, approvals, retry counts, timestamps, and final artifacts.

Suggested stages:

1. Requirement intake and ambiguity/risk classification.
2. Requirement normalization and acceptance criteria.
3. Task decomposition and dependency graph creation.
4. Architecture/change-impact proposal.
5. Implementation artifact proposal or bounded code/tool execution.
6. Test generation/execution and result review.
7. Documentation/release-readiness review.
8. Human final approval and completion.

Required orchestration behavior:

- Validate entry/exit conditions for each stage.
- Run independent read-only analysis stages in parallel; synchronize before dependent work.
- Persist stage state and decision lineage so work can resume and be audited.
- Require human approval before high-impact changes, external publication, destructive actions, or release.
- Bound retries and stage duration; define fallback and safe-stop outcomes.
- Define rollback/compensation for actions that change files or external state. If an action cannot be rolled back, require approval before it starts.
- Re-plan downstream tasks when an upstream requirement/decision changes and preserve the old plan/version in the audit history.
- Record structured events: workflow/stage IDs, inputs/outputs or their hashes, decisions, model/tool versions, approvals, errors, retries, and elapsed time. Never log secrets or unnecessary personal data.
- Track success rate, retry/rollback counts, end-to-end latency, and MTTR with explicit definitions and a reporting endpoint or dashboard.

Semantic Kernel should provide bounded requirement analysis/planning and invoke explicitly allow-listed tools. Deterministic policy, validation, approvals, state transitions, and persistence remain ordinary application code. Do not rely on prompt text or keyword filtering as the security boundary.

**Exit gate:** tests demonstrate sequential stages, parallel execution plus synchronization, blocked execution pending approval, bounded retry/fallback, safe-stop, resumed workflow, and downstream re-plan after an upstream change. Tests must run without a live LLM by using a fake agent adapter.

### 3. Demonstrate the Three Scenarios

Keep these as executable examples or integration tests, not documentation-only claims:

- **Greenfield:** propose and execute a new URL-shortener capability from requirement through tests and docs; show task graph and approval state.
- **Brownfield:** change an existing behavior (for example, click analytics); show impacted components, affected tests, change proposal, and regression results.
- **Ambiguous:** submit an underspecified requirement; show the ambiguity/risk assessment, clarification checkpoint or explicitly recorded assumptions, then re-plan after clarification.

Each scenario should capture the requirement, normalized intent, tasks/dependencies, stage transitions, approvals, validation results, artifacts, and final summary.

### 4. Finish Documentation and Release Readiness

1. Update `ARCHITECTURE.md` with implemented component boundaries, workflow graph/control flow, persistence, security, and decision lineage.
2. Update `FUNCTIONALITY.md` to match actual endpoints and behavior.
3. Rewrite `ENGINEERING_SUMMARY.md` to separate implemented capabilities from limitations and assumptions.
4. Add `README.md` with prerequisites, setup, configuration, migrations/database setup, run commands, API examples, test commands, and scenario walkthroughs.
5. Add a release checklist: build/tests, configuration/secrets, database initialization, API smoke test, approval status, and known risks.

**Exit gate:** a reviewer can clone the repository, follow the README without undocumented setup, run tests, exercise the API, and reproduce all three scenarios.

## Security and Reliability Guardrails

- Use environment variables, .NET user secrets, or a secret manager for model credentials. Reject placeholder/missing credentials when agent functionality is invoked; allow non-agent URL APIs to run without a model key if intended.
- Apply input limits and structural validation. Treat model output as untrusted data and validate it against typed schemas before acting.
- Give the agent only allow-listed tools and least privilege. Require authorization and human approval for high-impact operations.
- Avoid claiming a prompt sanitizer prevents prompt injection. Prompt instructions are not an authorization mechanism.
- Add timeouts, cancellation, bounded retries with backoff where appropriate, idempotency for retried state-changing actions, and observable failure outcomes.
- Do not expose stack traces, credentials, prompts containing sensitive data, or internal paths through API responses or logs.

## Verification Checklist

- [ ] Root solution restores and builds with documented SDK.
- [ ] Unit and integration test projects run without external model credentials.
- [ ] Returned short URLs reach the redirect endpoint.
- [ ] Database schema is created/versioned reproducibly.
- [ ] Invalid and maliciously structured inputs are rejected deterministically.
- [ ] URL shortening collision behavior is bounded and tested.
- [ ] Analytics reads and click updates behave correctly under concurrent requests.
- [ ] Semantic Kernel function calling is configured and only allow-listed tools execute.
- [ ] Workflow graph, gates, approvals, retries, safe-stop, audit trail, metrics, and re-planning are implemented and tested.
- [ ] All three scenarios have reproducible artifacts and validation results.
- [ ] Documentation describes only verified behavior and identifies remaining limitations.
