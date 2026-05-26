# Specification: Remove unused `ICurrentUserService` dependency from `GridLayoutsController`

## Summary
`GridLayoutsController` accepts and stores an `ICurrentUserService` instance that none of its action methods ever read. This spec removes the dead constructor parameter and field so the controller's declared dependencies match its actual behavior. There is no functional change; identity resolution continues to happen inside the MediatR handlers, which inject `ICurrentUserService` directly.

## Background
The arch-review routine (filed 2026-05-25) flagged a dead constructor dependency in `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs`. The controller declares `private readonly ICurrentUserService _currentUserService;` and assigns it in the constructor, but `Get`, `Save`, and `Reset` only call `_mediator.Send(...)`. The user identity is resolved further down the stack by `GetGridLayoutHandler`, `SaveGridLayoutHandler`, and `ResetGridLayoutHandler`, each of which already injects `ICurrentUserService` on its own.

Keeping the unused dependency:
- Misleads readers into thinking the controller participates in identity resolution at the API boundary.
- Adds an unnecessary constructor parameter that consumers (DI container, tests) must satisfy.
- Violates YAGNI — the controller has no current or planned need for the service.

This is a small surgical cleanup, not a behavior change. Authorization is still enforced by the `[Authorize]` attribute on the controller and by the handlers' own use of `ICurrentUserService`.

## Functional Requirements

### FR-1: Remove the `ICurrentUserService` constructor parameter
The `GridLayoutsController` constructor must accept only `IMediator`.

**Acceptance criteria:**
- The constructor signature is `public GridLayoutsController(IMediator mediator)`.
- The constructor body assigns only `_mediator`.

### FR-2: Remove the `_currentUserService` field
The unused private field must be removed.

**Acceptance criteria:**
- `private readonly ICurrentUserService _currentUserService;` no longer exists in the file.
- No reference to `_currentUserService` remains in `GridLayoutsController.cs`.

### FR-3: Remove the now-unused `using` directive
The `using Anela.Heblo.Domain.Features.Users;` directive in `GridLayoutsController.cs` exists solely to resolve `ICurrentUserService`. Once the field is removed, the using must be removed too — unless another type from that namespace is still referenced in the file (none is at present).

**Acceptance criteria:**
- `using Anela.Heblo.Domain.Features.Users;` is removed from the top of `GridLayoutsController.cs`.
- `dotnet build` succeeds with no warnings about the removed using.

### FR-4: Preserve action method behavior
The three action methods (`Get`, `Save`, `Reset`) must continue to function identically — same routes, same HTTP verbs, same request/response shapes, same status codes.

**Acceptance criteria:**
- `[HttpGet("{gridKey}")] Get(string gridKey)` returns `ActionResult<GridLayoutDto?>` via `_mediator.Send(new GetGridLayoutRequest { GridKey = gridKey })`.
- `[HttpPut("{gridKey}")] Save(string gridKey, [FromBody] SaveGridLayoutRequest body)` sets `body.GridKey = gridKey`, sends via mediator, returns 500 on `!response.Success`, else `Ok()`.
- `[HttpDelete("{gridKey}")] Reset(string gridKey)` returns 500 on `!response.Success`, else `Ok()`.
- The `[Authorize]` attribute remains on the controller.
- Routing (`api/[controller]`) is unchanged.

### FR-5: Handlers remain untouched
The three MediatR handlers (`GetGridLayoutHandler`, `SaveGridLayoutHandler`, `ResetGridLayoutHandler`) must not be modified. They continue to resolve identity via their own `ICurrentUserService` injection.

**Acceptance criteria:**
- No files under `backend/src/Anela.Heblo.Application/Features/GridLayouts/` are modified.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. DI resolves one fewer dependency per controller instantiation, which is negligible.

### NFR-2: Security
No change. Authorization remains enforced by:
- The `[Authorize]` attribute on `GridLayoutsController`.
- `ICurrentUserService` consumption inside each MediatR handler.

The removal does not weaken any boundary check, because the controller was not performing any check with the removed dependency.

### NFR-3: Compatibility
- The controller's public HTTP contract (routes, verbs, request/response shapes, status codes) is unchanged.
- DI registration of `ICurrentUserService` is not modified; other consumers (the handlers) keep using it.
- No database migration. No configuration change.

### NFR-4: Validation gates
The change must pass the standard project validation gates listed in `CLAUDE.md`:
- `dotnet build` succeeds with no new warnings.
- `dotnet format` produces no diff.
- All existing tests pass.

## Data Model
No data model changes. `GridLayoutDto`, `GetGridLayoutRequest`, `SaveGridLayoutRequest`, `ResetGridLayoutRequest`, and the underlying persistence entities are all untouched.

## API / Interface Design

**Files modified (exactly one):**
- `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs`

**Before (relevant excerpt):**
```csharp
using Anela.Heblo.Domain.Features.Users;
// ...
public class GridLayoutsController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUserService;

    public GridLayoutsController(IMediator mediator, ICurrentUserService currentUserService)
    {
        _mediator = mediator;
        _currentUserService = currentUserService;
    }
    // action methods unchanged
}
```

**After:**
```csharp
// using Anela.Heblo.Domain.Features.Users; removed
public class GridLayoutsController : BaseApiController
{
    private readonly IMediator _mediator;

    public GridLayoutsController(IMediator mediator)
    {
        _mediator = mediator;
    }
    // action methods unchanged
}
```

**HTTP surface** — unchanged:
- `GET    /api/GridLayouts/{gridKey}` → `GridLayoutDto?`
- `PUT    /api/GridLayouts/{gridKey}` (body: `SaveGridLayoutRequest`) → 200 / 500
- `DELETE /api/GridLayouts/{gridKey}` → 200 / 500

## Dependencies
- `IMediator` (retained) — already wired in DI.
- `ICurrentUserService` (removed from this controller; still registered for handler use).
- No new packages. No new services.

## Testing

### Test scope
There are no existing tests for `GridLayoutsController` (confirmed via `backend/test/**/GridLayout*` — no matches). The change is a pure removal of unused code, so no new controller tests are required to validate the refactor itself.

### Validation
- `dotnet build` (whole solution) — must succeed cleanly.
- `dotnet format` — must produce no diff.
- `dotnet test` — full backend suite must remain green; integration tests that exercise the GridLayouts endpoints (if any) must continue to pass without modification, since the public surface is unchanged.
- Optional smoke check: hit `GET /api/GridLayouts/{gridKey}` against a running instance to confirm the endpoint still resolves through DI.

## Out of Scope
- Modifying the MediatR handlers or the `ICurrentUserService` interface/implementation.
- Auditing other controllers for the same pattern. (If a follow-up sweep is desired, it should be filed separately by the arch-review routine.)
- Frontend changes — no client-side contract is affected.
- Adding new tests for behavior that was not previously covered.
- DI registration changes.

## Open Questions
None.

## Status: COMPLETE
