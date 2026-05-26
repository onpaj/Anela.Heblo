# Architecture Review: Remove unused `ICurrentUserService` dependency from `GridLayoutsController`

## Skip Design: true

## Architectural Fit Assessment
The change strengthens, rather than challenges, the existing architecture. The codebase follows MediatR + Vertical Slice: thin MVC controllers translate HTTP to `IRequest`/`IRequest<T>` and forward via `_mediator.Send(...)`, while identity resolution, authorization context, and persistence are owned by handlers under `Anela.Heblo.Application/Features/<Slice>/UseCases/`. Verified directly: `GetGridLayoutHandler` (lines 14, 20, 26) injects `ICurrentUserService` and reads `GetCurrentUser()` itself; the controller's field is assigned and never read. The proposed removal eliminates a constructor parameter that crosses the slice boundary unnecessarily and aligns this controller with the rest of the API surface. No integration points change — the HTTP contract, DI graph, authorization pipeline, and handler signatures are all untouched.

## Proposed Architecture

### Component Overview
```
HTTP Request
   │
   ▼
[Authorize] ── GridLayoutsController (IMediator only)
                       │  _mediator.Send(request)
                       ▼
              MediatR pipeline
                       │
                       ▼
        Get/Save/ResetGridLayoutHandler
            ├── ICurrentUserService  ◄── identity resolved here
            └── IGridLayoutRepository
                       │
                       ▼
                PostgreSQL (per-user layout JSON)
```
The only structural change is the deletion of the dotted edge `GridLayoutsController → ICurrentUserService`, which carried no behavior.

### Key Design Decisions

#### Decision 1: Remove the dependency rather than start using it
**Options considered:**
1. Remove the dead field and constructor parameter (spec proposal).
2. Keep the field and add controller-level identity logging or pre-handler guard clauses.
3. Move identity resolution up from handlers to the controller and pass `userId` into each request DTO.

**Chosen approach:** Option 1 — remove.

**Rationale:** Option 2 invents a requirement that doesn't exist (YAGNI) and would couple HTTP concerns to identity reading. Option 3 is an architectural inversion: it would force every other handler to be refactored to accept `userId` from outside, weakening encapsulation of the slice and contradicting the established pattern verified in `GetGridLayoutHandler`. Removal matches the project's "surgical changes" rule in `CLAUDE.md` and keeps the controller a pure HTTP-to-mediator adapter.

#### Decision 2: Leave handlers and DI registration as-is
**Options considered:**
1. Touch only `GridLayoutsController.cs`.
2. Audit and refactor the other controllers / handlers for the same pattern in this PR.

**Chosen approach:** Option 1.

**Rationale:** Spec explicitly scopes to one file; broader audit is listed Out of Scope and should be filed as a separate arch-review item. Bundling unrelated cleanup would violate the "surgical changes" directive and inflate review surface.

## Implementation Guidance

### Directory / Module Structure
No new files. No directory changes. Single-file edit:
- `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs`

### Interfaces and Contracts
No interface or contract changes. Specifically preserved:
- `BaseApiController` inheritance.
- `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]` attributes on the class.
- Action method signatures, routes, verbs, and response types: `Get(string) → ActionResult<GridLayoutDto?>`, `Save(string, SaveGridLayoutRequest) → ActionResult`, `Reset(string) → ActionResult`.
- `ICurrentUserService` itself, its DI registration, and all three handlers.

Edits required (verified against the file at lines 1–24):
- Delete line 5 (`using Anela.Heblo.Domain.Features.Users;`).
- Delete line 18 (`private readonly ICurrentUserService _currentUserService;`).
- Change constructor signature on line 20 to `public GridLayoutsController(IMediator mediator)`.
- Delete line 23 (`_currentUserService = currentUserService;`).

### Data Flow
Unchanged at the runtime level. For each of `Get`/`Save`/`Reset`:
1. ASP.NET Core authenticates the caller via `[Authorize]`.
2. Controller wraps the route value into the appropriate `IRequest` and calls `_mediator.Send`.
3. Handler reads identity via `_currentUserService.GetCurrentUser()`, derives `userId` (Id ?? Email, throws if both null — see `GetGridLayoutHandler:27`), and calls `IGridLayoutRepository`.
4. Handler returns a response; controller maps `Success == false` to HTTP 500 (for Save/Reset) or returns the layout (for Get).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Other consumers (custom DI overrides, test fixtures) construct `GridLayoutsController` with the two-arg constructor | Low | Verified via `grep` — only `GridLayoutsController.cs` references `_currentUserService` in `backend/src/Anela.Heblo.API/Controllers`; no controller tests exist. `dotnet build` will catch any straggling construction site. |
| Stale `using` left behind triggers analyzer warnings | Low | FR-3 mandates removing the `using`; `dotnet build` and `dotnet format` validation gates will surface any residual warning. |
| Reviewer assumes controller still enforces identity | Low | `[Authorize]` remains; identity reading lives in handlers (verified). Document in PR description that authorization surface is unchanged. |
| Hidden reflection-based or DI-container-side resolution of the old constructor | Very Low | Project uses standard `Microsoft.Extensions.DependencyInjection` constructor injection; ASP.NET Core controller activation resolves by best-matching public constructor and will pick the new single-arg form without configuration changes. |

## Specification Amendments
None required. The spec is accurate against the current file (verified line-by-line) and correctly bounded. Status: COMPLETE is appropriate.

## Prerequisites
None. No migration, no configuration change, no infrastructure change, no new package. The change can begin immediately and is gated only by the standard validation suite in `CLAUDE.md`:
- `dotnet build` clean (no new warnings, including no "unused using" diagnostics).
- `dotnet format` no diff.
- `dotnet test` green (no GridLayout-specific tests exist — confirmed via `Glob backend/test/**/GridLayout*`).