# Architecture Review: Resolve `ReplayedBy` Identity Inside `ReplayWebhookEventHandler`

## Skip Design: true
This is a backend-only, no-UI, single-handler surgical fix correcting an arch-review-flagged ADR-005 violation. It touches three existing files (`SmartsuppWebhookAuditController.cs`, `ReplayWebhookEventRequest.cs`, `ReplayWebhookEventHandler.cs`) plus their test file, follows an established codebase pattern verbatim (`CreateAdjustmentHandler`), and introduces no new component, module, contract, or user-facing behavior. There is nothing here for a design phase to add.

## Architectural Fit Assessment
The spec is architecturally sound and requires no amendment. It correctly identifies the violation, the fix location, and the reference pattern.

Confirmed by direct inspection:
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs:67-72` — the controller currently reads `User.Identity?.Name ?? "unknown"` and stamps it into `ReplayWebhookEventRequest.ReplayedBy` before calling `_mediator.Send`. This is a direct instance of the pattern ADR-005 (`docs/architecture/development_guidelines.md:288`) and its "User Identity Resolution" practices section (`docs/architecture/development_guidelines.md:63-79`) prohibit: *"Controllers never resolve identity — no `GetCurrentUserId()` helper, no `ICurrentUserService` injection, no stamping `UserId`/`ModifiedBy` onto requests."*
- `ReplayWebhookEventRequest.cs:8` declares `public string ReplayedBy { get; set; } = "";` — exactly the "client-settable `UserId`/`ModifiedBy`-style field" ADR-005 disallows on request DTOs.
- `ReplayWebhookEventHandler.cs:56` consumes it verbatim: `entry.LastReplayedBy = request.ReplayedBy;`.
- The prescribed replacement pattern is already live in `CreateAdjustmentHandler.cs` (`backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/CreateAdjustment/CreateAdjustmentHandler.cs:13,20,26,61`): `ICurrentUserService` is injected via constructor and `_currentUserService.GetCurrentUser().Name ?? "unknown"` is called inside `Handle` to stamp an audit field (`CreatedBy`). `ReplayWebhookEventHandler` should do the same for `LastReplayedBy`.
- `ICurrentUserService` (`backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`) exposes `CurrentUser GetCurrentUser()`; `CurrentUser` (`.../Users/CurrentUser.cs`) is `public record CurrentUser(string? Id, string? Name, string? Email, bool IsAuthenticated)`. `.Name` is nullable — the `?? "unknown"` fallback is required, matching `CreateAdjustmentHandler`'s exact expression.
- DI: `ICurrentUserService` is already registered in `UsersModule.cs` (`AddUsersModule()`) per ADR-005's table; `ReplayWebhookEventHandler` needs no new module wiring — constructor injection alone is sufficient, consistent with how 60+ other handlers consume it.

No new abstractions, no new module boundaries, no persistence/schema change (`SmartsuppWebhookAuditEntry.LastReplayedBy` remains an unchanged `string` column). The change is a mechanical relocation of an identity read from controller to handler, replacing the read's source (`User.Identity?.Name` → `ICurrentUserService.GetCurrentUser().Name`) and removing the now-obsolete DTO field and its plumbing.

## Proposed Architecture

### Component Overview
No new components. Three existing files change shape (not shape of the module):
- `SmartsuppWebhookAuditController.cs` — `Replay` action loses its identity-resolution line and now sends `new ReplayWebhookEventRequest { Id = id }`.
- `ReplayWebhookEventRequest.cs` — loses the `ReplayedBy` property; retains `Id`.
- `ReplayWebhookEventHandler.cs` — gains an `ICurrentUserService` constructor dependency and resolves `LastReplayedBy` internally.
- `ReplayWebhookEventHandlerTests.cs` — gains an `ICurrentUserService` mock, drops `ReplayedBy` from all three test's request construction.

### Key Design Decisions

#### Decision 1: Where to resolve identity
**Options considered:**
1. Keep resolution in the controller, just rename the field (rejected — doesn't fix the ADR-005 violation, only cosmetic).
2. Resolve identity inside `ReplayWebhookEventHandler` via injected `ICurrentUserService` (the codebase's single sanctioned pattern).
3. Introduce a shared `UserIdResolver`/base-handler helper to centralize the `?? "unknown"` fallback across handlers (rejected — ADR-005 explicitly says "Do not add a shared `UserIdResolver` helper unless a real consumer exists"; this is a single-field, single-handler fix, not a cross-cutting refactor).

**Chosen approach:** Option 2 — inject `ICurrentUserService` directly into `ReplayWebhookEventHandler`'s constructor, call `_currentUserService.GetCurrentUser().Name ?? "unknown"` inline in `Handle`, exactly mirroring `CreateAdjustmentHandler.cs:61`.

**Rationale:** This is the codebase's one documented, tested, 60+-handler-proven convention for this exact concern (ADR-005). Introducing anything else (a helper, a different fallback string, a different DI shape) would create a second variant of an already-converged pattern.

#### Decision 2: Whether `ReplayedBy` stays on the request DTO in any form
**Options considered:**
1. Keep `ReplayedBy` on `ReplayWebhookEventRequest` but stop the controller from populating it, defaulting to `""` and having the handler override it (rejected — dead property on a DTO that arch-review or a future contributor would rediscover and misuse; also violates "Request DTOs carry no client-settable `UserId`/`ModifiedBy`" literally, since the property still exists and is technically settable by any caller of the DTO, including future controller code or tests).
2. Remove `ReplayedBy` from `ReplayWebhookEventRequest` entirely (spec's FR-1, and ADR-005's explicit rule).

**Chosen approach:** Option 2 — delete the property outright.

**Rationale:** ADR-005's rule is about the DTO's shape, not just the controller's runtime behavior — a lingering unused property is exactly the kind of drift the ADR was written to prevent from creeping back in.

## Implementation Guidance

### Directory / Module Structure
No structural change. All edits are in-place within the existing Vertical Slice:
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs`

### Interfaces and Contracts

`ReplayWebhookEventRequest` (after fix):
```csharp
public class ReplayWebhookEventRequest : IRequest<ReplayWebhookEventResponse>
{
    public Guid Id { get; set; }
}
```

`ReplayWebhookEventHandler` constructor (after fix), matching `CreateAdjustmentHandler`'s field/constructor ordering convention:
```csharp
private readonly ApplicationDbContext _context;
private readonly IMediator _mediator;
private readonly ICurrentUserService _currentUserService;

public ReplayWebhookEventHandler(
    ApplicationDbContext context,
    IMediator mediator,
    ICurrentUserService currentUserService)
{
    _context = context;
    _mediator = mediator;
    _currentUserService = currentUserService;
}
```
Add `using Anela.Heblo.Domain.Features.Users;` to the handler file.

In `Handle`, replace:
```csharp
entry.LastReplayedBy = request.ReplayedBy;
```
with:
```csharp
entry.LastReplayedBy = _currentUserService.GetCurrentUser().Name ?? "unknown";
```

`SmartsuppWebhookAuditController.Replay` (after fix):
```csharp
public async Task<ActionResult<ReplayWebhookEventResponse>> Replay(
    Guid id,
    CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new ReplayWebhookEventRequest { Id = id }, cancellationToken);
    return HandleResponse(response);
}
```
No `using` cleanup needed beyond removing the now-unused local variable — the controller doesn't currently reference any `Users`-namespace type to remove.

DI: no change to `UsersModule.cs` or any module registration file — `ICurrentUserService` is already resolvable wherever `ReplayWebhookEventHandler` is constructed via MediatR's standard scoped-handler resolution.

### Data Flow
Before: `HttpContext.User` (ASP.NET Core auth) → controller reads `User.Identity?.Name` → `ReplayWebhookEventRequest.ReplayedBy` → handler copies to `entry.LastReplayedBy`.

After: `HttpContext.User` → `CurrentUserService` (via `IHttpContextAccessor`, outer-ring implementation in `Anela.Heblo.API/Features/Users/`) → `ICurrentUserService.GetCurrentUser()` called **inside** `ReplayWebhookEventHandler.Handle` → `entry.LastReplayedBy`. The request/response wire shape and route are otherwise unchanged; this only relocates *where* identity is read, unifying it with the claim-priority chain every other audited handler uses.

Test-side data flow: `ReplayWebhookEventHandlerTests` constructs a `Mock<ICurrentUserService>` stubbing `GetCurrentUser()` to return a `CurrentUser(Id, Name, Email, IsAuthenticated)` (per the pattern in `CreateJournalEntryHandlerTests.cs`), passes `_currentUserServiceMock.Object` as the handler's third constructor argument, and asserts `entry.LastReplayedBy` equals the mock's `Name`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file has 3 call sites constructing `ReplayWebhookEventRequest { ReplayedBy = ... }` and 2 constructing `ReplayWebhookEventHandler(ctx, mediator)` — easy to miss one during the mechanical edit | Low | All three request-construction sites (lines 48, 76, 99) and both handler-construction sites (lines 46, 73, 97) are enumerated above; compiler will fail loudly on any missed `ReplayedBy` reference or missing constructor arg since the property/parameter no longer exist |
| A future caller could still expect `ReplayedBy` on the DTO (external consumers, generated OpenAPI clients) | Low | This is an internal admin-only MediatR request type, not exposed directly in the public request body (route is `POST .../replay` with only `{id}` in the path, no request body) — removing the field is not a client-facing breaking change. Confirm the frontend TS client (if it has a generated `ReplayWebhookEventRequest` type) has no request-body usage of `replayedBy`; if none exists (expected, since the field was controller-populated, not client-supplied), no frontend change is needed |
| Fallback string divergence (`"unknown"` vs some other sentinel) | Low | Spec and `CreateAdjustmentHandler` both use the literal `"unknown"` — reuse verbatim, no new constant needed for a single call site |

## Specification Amendments
None. The spec's FR-1 (including its acceptance criteria) is fully consistent with the actual source files, ADR-005's text, and the `CreateAdjustmentHandler` reference pattern confirmed above. No changes to the spec are required.

## Prerequisites
None. `ICurrentUserService` and its DI registration already exist and require no setup; no other in-flight work in this area was found.
