```markdown
# Architecture Review: Extract `GetCurrentUserId` into `BaseApiController`

## Skip Design: true

Backend-only consolidation refactor. No new visual components, screens, layout changes, or user-facing surface alterations. The HTTP API contract is unchanged.

## Architectural Fit Assessment

The proposal aligns cleanly with the existing structure.

- **`BaseApiController` is the correct host.** It is declared `public abstract` in `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` and already exposes cross-cutting protected members (`Logger`, `HandleResponse<T>`). Adding `GetCurrentUserId` here is consistent with the file's stated purpose ("common functionality for all API controllers").
- **All three target controllers already inherit from `BaseApiController`.** No inheritance chain rewiring is required — the method becomes available the moment it is added.
- **Verified scope of the duplication.** A repo-wide grep for `GetCurrentUserId` in `backend/src/Anela.Heblo.API/Controllers/` returns exactly the three controllers the spec names (`DashboardController.cs`, `CarrierCoolingController.cs`, `GiftSettingsController.cs`); the only other hit anywhere in `backend/` is `CurrentUserService.cs`, which the spec correctly excludes. Similarly, the inline claim chain `FindFirst(ClaimTypes.NameIdentifier) ?? "sub" ?? "oid"` exists in exactly four files: the three controllers and `CurrentUserService`. The sweep contract in FR-5 is therefore satisfiable.
- **`CurrentUserService` is correctly out of scope.** It is DI-injected via `IHttpContextAccessor`, returns a nullable `CurrentUser` DTO, and serves application-layer handlers (110 referencing files). The controller helper serves a different shape (non-null id, throw-on-missing, no DI) and the two are not duplicates of *intent* — only of the underlying claim chain. Merging them would change a wide blast radius for no benefit.

The main integration point is the inheritance contract between `BaseApiController` and its subclasses — a `protected` instance member resolves at the call sites without any further wiring.

## Proposed Architecture

### Component Overview

```
ControllerBase (ASP.NET Core)
        │
        ▼
BaseApiController                              (anela.heblo.api/Controllers)
  ├── Logger                  (existing)
  ├── HandleResponse<T>(...)  (existing)
  └── GetCurrentUserId()      (NEW — protected, instance, throws UnauthorizedAccessException)
        │
        ├── DashboardController       (5 call sites — already present)
        ├── CarrierCoolingController  (1 call site  — already present)
        └── GiftSettingsController    (1 call site  — already present)

Out of scope (unchanged):
  ICurrentUserService / CurrentUserService          (DI path, nullable id, ~110 consumers)
  HangfireDashboardAuthorizationFilter (if present) (filter pipeline, cannot inherit)
```

### Key Design Decisions

#### Decision 1: Host on `BaseApiController` (instance member) vs. static helper or extension method
**Options considered:**
- (A) Protected instance method on `BaseApiController`.
- (B) Static helper accepting `ClaimsPrincipal` (e.g. in a `ClaimsPrincipalExtensions` class).
- (C) Inject `ICurrentUserService` into each controller and call `.GetCurrentUser().Id`.

**Chosen approach:** (A), as the spec proposes.

**Rationale:**
- The codebase has already declared `BaseApiController` exactly to host this kind of cross-controller helper; adding `Logger` and `HandleResponse<T>` set the precedent.
- (B) loses the `User` capture and forces every call site to type `User.GetCurrentUserId()`, which is uglier than `GetCurrentUserId()` for negligible testability gain.
- (C) is wrong here: `CurrentUserService` returns `Id` as `string?`, the controllers need non-null, so each call site would still need a null-check or re-throw — the duplication just moves up a layer. Worse, it would require revisiting DI registration for three controllers for no architectural benefit. The spec's "out of scope" framing on this is correct.

#### Decision 2: Throw `UnauthorizedAccessException` rather than return `Unauthorized()` or `null`
**Options considered:**
- (A) `throw new UnauthorizedAccessException(...)` — spec choice.
- (B) Change the helper's return type to `string?` (mirroring `ICurrentUserService`).
- (C) Have the helper return `IActionResult` / `ActionResult<T>` short-circuit on failure.

**Chosen approach:** (A).

**Rationale:**
- All three existing call sites consume `GetCurrentUserId()` as a non-null `string` and immediately assign it to a request DTO. Returning nullable (B) would require every call site to add a null-check or `!` — strictly worse than today.
- (C) would change the ergonomics of the call sites (early-return pattern) and require touching all 7 call sites. The spec is explicit that mapping unauthenticated callers to a proper 401 is out of scope.
- `UnauthorizedAccessException` is the standard BCL exception for this scenario and replaces both `Exception` (Dashboard) and `InvalidOperationException` (the other two) with one consistent, semantically correct type. The HTTP-status side effect is unchanged (500 in all four cases — before and after).

#### Decision 3: Coverage strategy — keep tests in `DashboardControllerTests`, no new file
**Options considered:**
- (A) Add `BaseApiControllerTests.cs` with a minimal test-only subclass exposing the protected member.
- (B) Leave the four existing tests in `DashboardControllerTests` and only update the failure-path assertion.

**Chosen approach:** (B), per FR-7.

**Rationale:**
- Confirmed: `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` does **not** exist. The FR-7 guard ("only if file already exists") therefore correctly skips it.
- The four tests in `DashboardControllerTests` already exercise the behavior end-to-end through a real controller subclass. They remain valid coverage after the method moves up the inheritance chain.
- Creating a parallel test fixture would introduce a new pattern (test-only controller subclasses) that the repo does not currently use.

## Implementation Guidance

### Directory / Module Structure

No new files. Modifications only:

```
backend/src/Anela.Heblo.API/Controllers/
  ├── BaseApiController.cs         [MODIFY] add using + GetCurrentUserId()
  ├── DashboardController.cs        [MODIFY] remove private method; drop `using System.Security.Claims;`
  ├── CarrierCoolingController.cs   [MODIFY] remove private method; drop `using System.Security.Claims;`
  └── GiftSettingsController.cs     [MODIFY] remove private method; drop `using System.Security.Claims;`

backend/test/Anela.Heblo.Tests/Controllers/
  └── DashboardControllerTests.cs   [MODIFY] update WhenNoClaimsPresent assertion
```

Verified that none of the three controller files reference `System.Security.Claims` anywhere other than the private helper they will be losing — the `using` directive can be removed cleanly in all three.

### Interfaces and Contracts

```csharp
// BaseApiController.cs
using System.Security.Claims;
// ...

/// <summary>
/// Gets the authenticated user's id from the standard claim chain
/// (NameIdentifier → sub → oid). Throws <see cref="UnauthorizedAccessException"/>
/// when no id claim is present.
/// </summary>
protected string GetCurrentUserId()
    => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
       ?? User.FindFirst("sub")?.Value
       ?? User.FindFirst("oid")?.Value
       ?? throw new UnauthorizedAccessException("Authenticated user has no identifiable claim.");
```

Rules subclasses must follow:
- Do **not** redeclare a `GetCurrentUserId` member on a subclass (it would shadow the inherited one and silently re-introduce the divergence problem).
- Do **not** catch `UnauthorizedAccessException` in controller code to convert it to another exception — if a 401 mapping is ever needed, do it in middleware so every caller benefits.

### Data Flow

For each modified call site:

```
HTTP Request (Authorize) → Controller action
                              │
                              ▼
                       GetCurrentUserId()       (inherited from BaseApiController)
                              │
            ┌─────────────────┼─────────────────┐
            ▼                 ▼                 ▼
  ClaimTypes.NameIdentifier  "sub"            "oid"
            │                 │                 │
            └─────── first non-null wins ───────┘
                              │
                  ┌───────────┴───────────┐
                  ▼                       ▼
            string userId           UnauthorizedAccessException
                  │                       │
                  ▼                       ▼
       MediatR request.UserId       ASP.NET unhandled → 500
       (handler proceeds)           (same as today)
```

No data-flow change for the success path. The failure path now produces a single exception type instead of three different ones.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A subclass already (or later) defines a non-virtual `GetCurrentUserId()` and silently hides the base member, defeating the consolidation. | Low | The compiler emits CS0108 (`new` keyword required to hide…) as a warning. Verified at grep time that no other controller currently has the method. Reviewers should treat any CS0108 on this name as a blocker. |
| A consumer (production, not tests) catches `Exception` or `InvalidOperationException` specifically and depends on the old type. | Low | Grep confirms no such catch outside the tests covered by FR-6. ASP.NET surfaces both the old and new exceptions as HTTP 500, so middleware/global handlers see equivalent behavior. |
| HTTP 500 on missing claims is a defense-in-depth posture, not a clean 401. | Medium | Already true in all three controllers today — not introduced by this refactor. Spec explicitly puts proper 401 mapping out of scope. Document the limitation in a follow-up issue if a 401 contract is desired later. |
| Future contributor adds another controller and re-introduces the private copy (because it isn't discoverable from outside `BaseApiController`). | Low | The XML `<summary>` on the new member makes IntelliSense show it on every subclass. Discoverability is at parity with `Logger` and `HandleResponse<T>`, both of which have survived the same risk. |
| `BaseApiController` accumulates unrelated auth helpers over time. | Low | Out of scope for this change. If the file grows past ~3 cross-cutting concerns, revisit by extracting an `IControllerUserContext` or claim-chain extension method then. |

## Specification Amendments

The spec is implementable as written. Two minor corrections / clarifications:

1. **FR-2 line-number drift.** The spec cites `DashboardController.cs:97-105` for the private method, which matches the current file (the brief says "97–105" inclusive of the closing brace). The cited five call sites (lines 38, 48, 58, 72, 86) also match. No change to the spec required — flagging only because re-running the implementation against a future revision of the file should re-check via grep, not by line number.
2. **FR-5 sweep is already satisfied by the proposed three deletions.** A repo-wide grep across `backend/src/Anela.Heblo.API/Controllers/` for both `GetCurrentUserId` and the literal three-step chain confirms only the three named controllers contain the duplicate; `CurrentUserService.cs` is the only other hit and is intentionally excluded. The implementer should still re-run the grep at completion time to verify nothing was added during the same branch window, but no additional file modifications are expected.
3. **Suggested addition to NFR-2.** Add a single line: *"If a global exception-handling middleware is later added that maps `UnauthorizedAccessException` → 401, this refactor will become the consolidation point. Treat that as a deliberate downstream improvement, not a hidden behavior change."* This makes the upgrade path explicit so a future reviewer doesn't perceive a regression.

## Prerequisites

None. The change requires:
- No database migration.
- No configuration / Key Vault change.
- No new NuGet package.
- No DI registration update (the helper is an instance member; no service container involvement).
- No frontend regeneration (HTTP contract unchanged).

Implementation can start immediately. Validation gates: `dotnet build`, `dotnet format`, `dotnet test` on the touched test project — all per `CLAUDE.md`.
```