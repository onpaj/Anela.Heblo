# Architecture Review: Refactor UpdateManufactureOrderStatusHandler to use ICurrentUserService

## Skip Design: true

Backend-only refactor of an Application-layer handler. No UI components, screens, layouts, HTTP contracts, or visible behavior changes. No design work required.

## Architectural Fit Assessment

The refactor aligns with an **established, dominant pattern** in the Application layer: ~50 handlers and services across the codebase already depend on `ICurrentUserService` (e.g. `UpdatePurchaseOrderStatusHandler`, `CreateManufactureOrderHandler`, `UpdateManufactureOrderHandler` — the latter two living in the *same* `Manufacture/UseCases` folder). `UpdateManufactureOrderStatusHandler` is currently the outlier; the proposed change removes that outlier rather than introducing anything novel.

Key integration points:
- **`ICurrentUserService`** (`backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`) — the abstraction the handler will depend on.
- **`CurrentUserService`** (`backend/src/Anela.Heblo.Application/Features/Users/CurrentUserService.cs`) — concrete impl, registered as **Singleton** in `Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130` (works because `HttpContext` is resolved through the singleton `IHttpContextAccessor` per-request).
- **`CurrentUserExtensions.GetDisplayName()`** (`backend/src/Anela.Heblo.Domain/Features/Users/CurrentUserExtensions.cs`) — the spec-mandated display-name helper. Returns `"System"` when unauthenticated, `user.Name ?? "Unknown User"` otherwise.
- **Two call-sites** inside the handler: `Handle()` line 64 (`order.StateChangedByUser = GetCurrentUserName()`) and `WriteDownInventoryAsync()` line 177 (`var user = GetCurrentUserName()`). The `request.Note` block on line 122 also derives from `order.StateChangedByUser` transitively, so it inherits the correction without code change.

There is one notable inconsistency in the codebase that this review surfaces (see Specification Amendments): the closest sibling, `UpdatePurchaseOrderStatusHandler`, uses `currentUser.Name ?? "System"` rather than `GetDisplayName()`. The spec correctly prescribes the extension method, which is the more abstraction-aware path.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────┐
│ API Layer (controller, auth pipeline)                          │
│  └─ Populates ClaimsPrincipal on HttpContext                   │
└────────────────────────────────────────────────────────────────┘
                          │ MediatR dispatch
                          ▼
┌────────────────────────────────────────────────────────────────┐
│ Application Layer                                              │
│                                                                │
│  UpdateManufactureOrderStatusHandler                           │
│   ├─ IManufactureOrderRepository       (existing)              │
│   ├─ IManufacturedProductInventoryRepository (existing)        │
│   ├─ TimeProvider                       (existing)             │
│   ├─ ILogger<...>                       (existing)             │
│   ├─ IConditionsReadingProvider         (existing)             │
│   └─ ICurrentUserService  ◄── REPLACES IHttpContextAccessor    │
│           │                                                    │
│           ▼                                                    │
│       CurrentUserService (Singleton)                           │
│           └─ IHttpContextAccessor (Singleton)                  │
└────────────────────────────────────────────────────────────────┘
                          │ writes
                          ▼
┌────────────────────────────────────────────────────────────────┐
│ Domain: ManufactureOrder.StateChangedByUser, Notes[].CreatedBy │
│ Domain: ManufacturedProductInventoryItem.CreatedBy             │
└────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Use `CurrentUserExtensions.GetDisplayName()` rather than ad-hoc `currentUser.Name ?? "System"`

**Options considered:**
1. Call `_currentUserService.GetCurrentUser().GetDisplayName()` (spec-mandated).
2. Inline `currentUser.Name ?? "System"` like `UpdatePurchaseOrderStatusHandler` does.

**Chosen approach:** Option 1 — the extension method.

**Rationale:** The extension exists specifically to centralise the "System for anonymous, name for authenticated" rule. Inlining the coalesce duplicates that rule and silently diverges if `GetDisplayName()` ever evolves (e.g. to prefer email when name is missing). The handler should consume the abstraction's full intent, not just its data record. This also nudges the codebase toward a single convention; the inline pattern in `UpdatePurchaseOrderStatusHandler` is a smell that should be cleaned up separately, not propagated.

#### Decision 2: Resolve the display name **once** per `Handle` invocation

**Options considered:**
1. Call `_currentUserService.GetCurrentUser().GetDisplayName()` at each call-site (2 sites: `Handle` line 64, `WriteDownInventoryAsync` line 177).
2. Resolve once at the top of `Handle`, store in a local `currentUserName`, pass into `WriteDownInventoryAsync` as a parameter.

**Chosen approach:** Option 2.

**Rationale:** The current code already exhibits this pattern — `WriteDownInventoryAsync` calls `GetCurrentUserName()` again, which re-walks the claims. Resolving once tightens consistency (the inventory `CreatedBy` is guaranteed to match `StateChangedByUser` for the same transition), simplifies testing (one mock setup), and matches the intent of "stamp this transition by this user." The cost is a single method parameter on a private helper.

#### Decision 3: Keep constructor parameter order stable except for the swap

**Options considered:**
1. Drop `IHttpContextAccessor`, insert `ICurrentUserService` in the same positional slot.
2. Move dependencies around to match some preferred ordering convention.

**Chosen approach:** Option 1.

**Rationale:** This is a surgical refactor (per CLAUDE.md "Surgical changes"). Re-ordering forces both test files to re-author all six positional arguments and creates noise in the diff. Slot-replacement keeps the change minimal.

#### Decision 4: Do not extend `CurrentUserService.GetCurrentUser()` Name-claim fallbacks

**Options considered:**
1. Adopt the spec as-written (use existing chain).
2. Extend the Name fallback in `CurrentUserService` to include `preferred_username` / `upn` so authenticated Entra ID users without a `name` claim land on their UPN instead of `"Unknown User"`.

**Chosen approach:** Option 1, but **flag a spec gap** (see Specification Amendments).

**Rationale:** The spec explicitly puts redefining `CurrentUserService` fallbacks Out of Scope. Honour that boundary. However, the spec's FR-4 acceptance criterion implicitly assumes a fallback that does not exist today — that gap belongs in the spec, not in this handler's refactor.

## Implementation Guidance

### Directory / Module Structure

No new files. Touched files:

```
backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/
  └── UpdateManufactureOrderStatusHandler.cs      (edit)

backend/test/Anela.Heblo.Tests/Features/Manufacture/
  ├── UpdateManufactureOrderStatusHandlerTests.cs            (edit — mock swap + add fallback test)
  └── UpdateManufactureOrderStatusHandlerConditionsTests.cs  (edit — mock swap)
```

No DI registration changes required. `ICurrentUserService` is already registered (`ServiceCollectionExtensions.cs:130`) and `IHttpContextAccessor` registration must remain — other consumers still depend on it.

### Interfaces and Contracts

**Constructor (new signature):**
```csharp
public UpdateManufactureOrderStatusHandler(
    IManufactureOrderRepository repository,
    TimeProvider timeProvider,
    ILogger<UpdateManufactureOrderStatusHandler> logger,
    ICurrentUserService currentUserService,        // ← swapped in
    IConditionsReadingProvider conditionsProvider,
    IManufacturedProductInventoryRepository inventoryRepository)
```

**Field:**
```csharp
private readonly ICurrentUserService _currentUserService;
```

**Resolution call (replaces `GetCurrentUserName()`):**
```csharp
var currentUserName = _currentUserService.GetCurrentUser().GetDisplayName();
```

**`using` directives:**
- Remove: `using Microsoft.AspNetCore.Http;`
- Add: `using Anela.Heblo.Domain.Features.Users;`

**Private method `GetCurrentUserName()`:** deleted.

**Private method `WriteDownInventoryAsync` signature:** add a `string changedByUser` parameter; remove its internal `var user = GetCurrentUserName();`.

### Data Flow

For a `Handle(UpdateManufactureOrderStatusRequest)` invocation:

1. Controller validates auth, MediatR routes the request.
2. Handler resolves `currentUserName = _currentUserService.GetCurrentUser().GetDisplayName()` **once**.
3. Handler reads the order; validates the state transition.
4. Handler mutates the order, stamping `order.StateChangedByUser = currentUserName`.
5. If a note is supplied, `Note.CreatedByUser` inherits `order.StateChangedByUser` (unchanged).
6. If transitioning to `Completed`, `WriteDownInventoryAsync(order, currentUserName, ct)` projects products into `ManufacturedProductInventoryItem(createdBy: currentUserName, …)`.
7. Repository persists the order; response carries `StateChangedByUser`.

Resolution path inside `CurrentUserService.GetCurrentUser()` (unchanged by this refactor):
- `Name`: `Identity.Name` → `ClaimTypes.Name` → `"name"` → `"Unknown User"` (authenticated) / `"Anonymous"` (anonymous).
- `IsAuthenticated`: drives `GetDisplayName()` to short-circuit to `"System"` when false.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Spec FR-4 acceptance criterion is unachievable as written.** `CurrentUserService.GetCurrentUser()` Name fallbacks are `Identity.Name → ClaimTypes.Name → "name"`. The `preferred_username` / `upn` / `oid` / `sub` claims feed Id/Email **only**, not Name. An authenticated request with only `preferred_username` will record `"Unknown User"`, not the UPN. | **HIGH** | Amend FR-4 (see below). Either (a) revise the test to assert `"Unknown User"` and reframe the bug fix as "stops recording `System` for authenticated users; records `Unknown User` when no Name claim is provided," or (b) file a follow-up to extend `CurrentUserService` Name fallbacks (out of scope per the spec; would require its own ADR). Without amendment, FR-4 will be written, then fail, then either get loosened in review or the implementer will silently broaden the `CurrentUserService` chain — both bad outcomes. |
| Singleton `ICurrentUserService` capturing per-request state could leak across requests. | LOW | Already mitigated — `CurrentUserService` itself stores no per-request state; it dereferences `IHttpContextAccessor.HttpContext` on every call. Existing 50+ consumers prove the pattern is safe. No change required. |
| Tests using positional constructor args break compilation if the slot order is wrong. | LOW | Replace `_httpContextAccessorMock.Object` with a `Mock<ICurrentUserService>` in the same positional slot in both test files. Both test files use named or positional args clearly — straightforward swap. |
| Existing test `Handle_WithoutHttpContext_ShouldUseSystemAsUser` (line 195) relies on `HttpContext == null` to produce `"System"`. | LOW | After refactor: mock `ICurrentUserService.GetCurrentUser()` to return `new CurrentUser(null, null, null, IsAuthenticated: false)`. `GetDisplayName()` will return `"System"`. Same assertion holds. |
| Inventory `CreatedBy` (`WriteDownInventoryAsync`) may diverge from `StateChangedByUser` if resolved twice and claims change mid-call. | LOW (theoretical) | Decision 2 above: resolve once, pass in. Eliminates the divergence vector. |
| Other code paths still depend on `IHttpContextAccessor` in the Application layer. | INFO | Spec Out of Scope. Issue #1716 tracks the broader effort; this refactor moves the needle from N to N-1. |

## Specification Amendments

### Amendment 1 — FR-4 acceptance criterion is unachievable as written (REQUIRED)

The spec states:

> A unit/integration test exercising a state change with a `ClaimsPrincipal` that has `preferred_username` but no `Identity.Name` records the `preferred_username` value (or the first matching fallback claim) in `StateChangedByUser`, not `"System"`.

This contradicts the actual implementation of `CurrentUserService.GetCurrentUser()`, which constructs `Name` only from `Identity.Name → ClaimTypes.Name → "name"`. The `preferred_username` claim is never inspected for the display name; it only contributes to `Email`.

**Required change:** Pick one of:

- **Amendment 1a (Recommended — keeps scope tight).** Rewrite the FR-4 acceptance criterion to: "A test exercising a state change with a `ClaimsPrincipal` whose `Identity.Name` is null but with `IsAuthenticated == true` records `"Unknown User"` (not `"System"`) in `StateChangedByUser`." This honours the existing fallback chain, still proves the bug is fixed (authenticated users no longer collapse to `"System"`), and changes nothing about `CurrentUserService`.
- **Amendment 1b.** Re-scope to include extending `CurrentUserService` Name fallbacks to `preferred_username` / `upn`. This is a separate architectural decision touching every consumer of `GetCurrentUser().Name` and should not piggyback on this refactor.

The brief itself (filed by the daily arch-review routine) is also slightly misleading on this point — it asserts that `CurrentUserService` applies a `preferred_username` / `upn` / `oid` / `sub` fallback chain, which is true for `Id` and `Email`, not for `Name`. The amendment should clarify this.

### Amendment 2 — Pass resolved user into `WriteDownInventoryAsync` (REQUIRED)

The spec describes the call-site replacement at FR-2 but does not specify whether the two call-sites independently call `_currentUserService.GetCurrentUser().GetDisplayName()` or share a single resolution. Per Decision 2, resolve once in `Handle`, pass to `WriteDownInventoryAsync` as a parameter. Add this as an implementation note to FR-2:

> The user name MUST be resolved at most once per `Handle` invocation and reused across all audit fields written for that transition.

### Amendment 3 — Note clarifying that audit "user" precision is improved but not perfected (INFO)

After this refactor, authenticated users whose token lacks a `name` / `Identity.Name` claim will be stamped as `"Unknown User"` rather than the bug's `"System"`. This is an improvement (`"Unknown User"` is at least distinguishable from background-job traffic in audit queries), but it is not the user's real identity. A follow-up may be warranted to broaden the Name fallback chain — that is a separate spec.

## Prerequisites

None. All required infrastructure already exists:

- `ICurrentUserService` interface defined (`backend/src/Anela.Heblo.Domain/Features/Users/ICurrentUserService.cs`).
- `CurrentUserService` registered as Singleton (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:130`).
- `CurrentUserExtensions.GetDisplayName()` defined (`backend/src/Anela.Heblo.Domain/Features/Users/CurrentUserExtensions.cs`).
- `IHttpContextAccessor` registration remains in place for other consumers — do not remove it.
- No database migration, no config change, no feature flag.

Validation before completion: `dotnet build`, `dotnet format`, both affected test classes (`UpdateManufactureOrderStatusHandlerTests`, `UpdateManufactureOrderStatusHandlerConditionsTests`) green.