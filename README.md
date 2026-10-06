# URL Shortener POC

## What this project does

This is a .NET URL-shortener prototype with a persisted engineering-workflow API and role-based Semantic Kernel agents whose instructions are stored as embedded Markdown resources.

**Current status:** the original Brownfield URL-shortener/workflow POC was developed first; the standalone Greenfield URL-shortener project was created afterward in `generated/greenfield/url-shortener`. URL shortening, redirect, click analytics, and a persisted workflow DAG are implemented. The Greenfield API now includes `GET /health/live`, verified by the Greenfield Project Implementer workflow. The dedicated Brownfield Project Implementer has also been exercised on a verified non-empty-diff policy change. Both new runs completed all 16 stages with final approval as POCs only; see the [Greenfield liveness evidence](docs/scenario-evidence/greenfield-liveness-agent-run.md) and [Brownfield agent evidence](docs/scenario-evidence/brownfield-implementer-agent-run.md). The older Greenfield workflow `58d44ded-9526-4c4e-9007-0c8e5a0f8681` remains a separate completed POC run that accepted the existing project as its baseline with zero code changes; see its [evidence](docs/scenario-evidence/greenfield-workflow-run.md). Live workflow stages continue to use deterministic fallback; only requirement analysis was verified once with Ollama Cloud `gemma4:31b`. None of these approvals authorizes production deployment.

## How a normal request is intended to work

1. A client sends a long URL to the shorten endpoint.
2. The API sends a command through MediatR.
3. The application handler checks for an existing mapping and creates one if needed.
4. The infrastructure repository saves the mapping in SQLite.
5. A visitor uses the short code; the API looks up the original URL, increments its click count, and redirects.

The intended request flow is:

`Client -> API Controller -> MediatR Handler -> Repository Interface -> EF Core/SQLite`

## Projects and folders

- `src/UrlShortener.Core`: URL entity and repository contract.
- `src/UrlShortener.Application`: commands, queries, handlers, DTOs, and application services.
- `src/UrlShortener.Infrastructure`: EF Core/SQLite persistence and short-code generation.
- `src/UrlShortener.Agents`: Semantic Kernel adapters, function-calling tools, and prompt loader.
- `agents/prompts`: version-controlled Markdown role instructions embedded into the Agents assembly at build time.
- `.github/agents`: VS Code custom agents shown in the Agent picker when `UrlShortenerSystem` is opened as the workspace folder. The Engineering Workflow coordinator and role agents are defined here; they are separate from the .NET runtime prompt pipeline. See [agents/PIPELINE.md](agents/PIPELINE.md) for the intended handoff order.
- Greenfield scaffolding is performed by the `Greenfield Project Scaffolder` agent under `generated/greenfield/<project-slug>` after architecture approval. After a separate implementation proposal approval, the `Greenfield Project Implementer` agent can add approved product behavior in that existing project and run its tests. The coordinator itself remains read-only. Agent path/command limits are prompt-level guidance, not a hard sandbox. The workflow API can independently hash configured source/test/generated files and compare the submitted `changedFiles` list; submitted test output is not verified unless trusted validation is enabled. Brownfield requests target the existing project and do not create a duplicate solution; after approval the `Brownfield Project Implementer` agent applies scoped changes (new endpoints, features, migrations) under `src/` and `tests/`. Both implementers must follow [agents/ARCHITECTURE_CONTRACT.md](agents/ARCHITECTURE_CONTRACT.md): endpoints are controller actions, never `Program.cs` minimal APIs.
- `src/UrlShortener.Api`: HTTP endpoints, configuration, and dependency registration.
- `tests/UrlShortener.Tests`: URL handler unit tests and HTTP integration tests using in-memory SQLite.

## API endpoints currently in the code

- `POST /api/url/shorten`: accepts JSON such as `{"originalUrl":"https://example.com"}`.
- `GET /api/url/redirect/{shortCode}`: looks up a code and redirects to its original URL.
- `GET /{shortCode}`: user-facing short-link route; redirects to the original URL.
- `GET /api/url/{shortCode}/analytics`: reports the URL, creation time, and click count.
- `POST /api/url/agent`: accepts a JSON string containing a natural-language request and passes it to the Semantic Kernel service.
- `POST /api/workflows`: analyze a requirement and create a persisted workflow graph.
- `GET /api/workflows` and `GET /api/workflows/{id}`: inspect workflow state, stages, plan versions, approvals, and audit events.
- `POST /api/workflows/{id}/stages/{stageId}/start` and `/result`: start a ready stage and submit its result.
- `POST /api/workflows/{id}/execute-ready`: execute all currently ready non-approval stages as a parallel proposal wave.
- `POST /api/workflows/{id}/stages/{stageId}/approval`: approve or reject gated work; requires `X-Approval-Token`.
- `POST /api/workflows/{id}/stages/{stageId}/replan`: version the plan after an upstream output changes.
- `POST /api/workflows/{id}/replan` and `/rollback`: revise a requirement or restore a previous plan; rollback requires `X-Approval-Token`.
- `GET /api/workflows/metrics`: report success rate, retries, plan rollbacks, recovery time, and latency.

## Technology choices

- .NET 10 target framework
- ASP.NET Core Web API
- Swagger UI via Swashbuckle.AspNetCore (Development environment only)
- MediatR for dispatching commands and queries
- Entity Framework Core with SQLite for local persistence
- Semantic Kernel 1.80.1/OpenAI integration is configured; runtime tool-calling still needs verification with valid model credentials

## Getting started

Install the .NET 10 SDK. From the repository root, restore and build the API project:

```powershell
dotnet restore .\src\UrlShortener.Api\UrlShortener.Api.csproj
dotnet build .\src\UrlShortener.Api\UrlShortener.Api.csproj
```

The API build has succeeded. Swagger is provided by the API project, Semantic Kernel/OpenAI dependencies belong to `UrlShortener.Agents`, and EF Core/SQLite dependencies belong to Infrastructure.

Run the URL handler unit tests with:

```powershell
dotnet test .\tests\UrlShortener.Tests\UrlShortener.Tests.csproj
```

Approval and plan-rollback endpoints fail closed unless `WorkflowGovernance:ApprovalToken` is configured and supplied as the `X-Approval-Token` header. If `WorkflowGovernance:RequiredRole` is also set, callers must supply the matching `X-Approval-Role` header. Configure governance with environment variables or user secrets; never commit a real token.

Run the API with:

```powershell
dotnet run --project .\src\UrlShortener.Api\UrlShortener.Api.csproj
```

When the API starts in the Development environment, open the address printed in the terminal and append `/swagger` to view the interactive API documentation. For example, if the terminal prints `http://localhost:5000`, open `http://localhost:5000/swagger`.

Do not add a real model API key or approval token to committed `appsettings.json`. Use .NET user secrets or environment variables. Development and testing initialize SQLite with `EnsureCreatedAsync`; production uses the checked-in EF migration through `Database.MigrateAsync()`.

Workflow artifacts are written as Markdown under `artifacts/workflows` by default. Trusted build/test execution is disabled by default; enable `WorkflowGovernance:AllowTrustedCommands` only for an operator-controlled workspace and set `WorkflowGovernance:TrustedWorkspaceRoot` explicitly.

## Documentation map

- [FUNCTIONALITY.md](FUNCTIONALITY.md): what each endpoint and layer does today, including known gaps.
- [ARCHITECTURE.md](ARCHITECTURE.md): layer responsibilities and dependency direction.
- [PROJECT_PLAN.md](PROJECT_PLAN.md): a plain-language sequence for improving the prototype.
- [ENGINEERING_SUMMARY.md](ENGINEERING_SUMMARY.md): design rationale and scenario write-up; update its claims as implementation changes.