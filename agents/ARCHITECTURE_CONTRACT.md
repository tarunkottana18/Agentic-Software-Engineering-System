# Architecture Contract

Binding rules for every implementation agent (Greenfield and Brownfield). The approved architecture proposal may add detail but may not weaken these rules. A violation is a defect, not a style choice; stop and request a revised proposal instead of deviating.

## Layers and dependency direction

`Api -> Application -> Core` and `Infrastructure -> Application, Core`. Core references nothing. Application never references Infrastructure or Api. Api references Application and Infrastructure only to compose DI.

| Layer | Owns | Must not contain |
|---|---|---|
| Core | Entities, domain exceptions, repository/domain interfaces | EF Core, ASP.NET, MediatR, HTTP types |
| Application | MediatR commands/queries + handlers, DTOs, validation, application interfaces, `AddApplication()` | EF Core, SQLite, HttpContext, controllers |
| Infrastructure | DbContext, migrations, repository implementations, generators, adapters, `AddInfrastructure()` | Controllers, business rules |
| Api | Controllers, request/response contracts, middleware, composition root | Business logic, data access, minimal-API endpoints |

## Endpoint rule (strict)

- Every HTTP endpoint is an action on a class deriving from `ControllerBase` with `[ApiController]`, placed in `Api/Controllers/`, one controller per resource.
- `Program.cs` may only configure services, middleware, and call `AddControllers()` / `MapControllers()`. Do NOT use `MapGet`, `MapPost`, `MapPut`, `MapDelete`, `MapGroup`, or inline lambdas to define endpoints. Short-link redirects are controller actions too.
- A controller action does only: map the request to a MediatR command/query, `Send` it through `ISender`, map the result to an HTTP response. No repositories, `DbContext`, or business rules in controllers.

## Use-case rule

- Each use case is a MediatR `IRequest<T>` (record) plus an `IRequestHandler<,>` in `Application/Features/<Feature>/Commands|Queries/`.
- Handlers depend only on Core/Application abstractions. Do not create parallel "service" classes that duplicate handler logic.
- New dependencies are added behind an interface in Core or Application and implemented in Infrastructure, then registered in that layer's `DependencyInjection.cs`.

## Persistence rule

- Schema changes require an EF Core migration in Infrastructure and an updated model snapshot. Concurrent counters/updates must be atomic at the database level.

## Tests and hygiene

- Every new behavior has a handler-level unit test and an API-level test through the controller.
- Remove template placeholders (`Class1.cs`) and dead code you replace. Do not leave unused duplicate implementations.
- No secrets in source or config. Validate input at the API/Application boundary.

## Conformance checklist (the implementer must report each item)

1. No `MapGet/MapPost/MapPut/MapDelete/MapGroup` in any `Program.cs`.
2. Every new/changed endpoint is a controller action; controller contains no data access or business logic.
3. Every new use case is a MediatR request + handler in the correct feature folder.
4. Project references still follow the dependency direction above.
5. New abstractions live in Core/Application; implementations in Infrastructure; DI registered in the owning layer.
6. Tests added for each acceptance criterion; build and tests actually run.
