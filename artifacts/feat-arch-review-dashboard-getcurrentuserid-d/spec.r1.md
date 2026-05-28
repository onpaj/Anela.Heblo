# Specification: Extract `GetCurrentUserId` into `BaseApiController`

## Summary
Three API controllers (`DashboardController`, `CarrierCoolingController`, `GiftSettingsController`) each declare their own private `GetCurrentUserId()` method with the same claim-fallback chain (`NameIdentifier` → `sub` → `oid`). This spec consolidates that logic into the existing `BaseApiController` as a single protected method, normalizes the failure mode to `UnauthorizedAccessException`, removes the private copies, and updates the affected tests.

## Background
`BaseApiController` (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`) is the established home for cross-controller utilities — it already provides `Logger` and `HandleResponse<T>`. The claim-fallback chain encodes a non-obvious Azure AD Entra rule (the `oid` claim is the stable user object id when `NameIdentifier`/`sub` are absent), so divergence between copies is a real risk.

Today's three copies are not literally identical: `DashboardController` throws `new Exception("User not found")` while `CarrierCoolingController` and `GiftSettingsController` throw `InvalidOperationException("Authenticated user has no identity claim.")`. None of them returns a proper auth failure. The arch-review finding correctly flags this as both a DRY violation and an error-handling inconsistency.

A separate abstraction, `CurrentUserService` (`backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs`), already encodes the same id-fallback chain for a different purpose (building a `CurrentUser` DTO via DI). That service is out of scope for this refactor — it serves a different consumer shape (DI-injected, nullable id, no throw) and the controller helper remains the most ergonomic option for command-handler controllers that need a non-null id inline.

## Functional Requirements

### FR-1: Add `GetCurrentUserId` to `BaseApiController`
Add one `protected` instance method to `BaseApiController` that returns the authenticated user's id, falling back through the standard claim chain.

**Behavior:**
- Read `User.FindFirst(ClaimTypes.NameIdentifier)?.Value` first.
- Fall back to `User.FindFirst("sub")?.Value`.
- Fall back to `User.FindFirst("oid")?.Value`.
- If all three are null/missing, throw `UnauthorizedAccessException` with message `"Authenticated user has no identifiable claim."`.

**Signature:**
```csharp
protected string GetCurrentUserId()
    => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
       ?? User.FindFirst("sub")?.Value
       ?? User.FindFirst("oid")?.Value
       ?? throw new UnauthorizedAccessException("Authenticated user has no identifiable claim.");
```

**Acceptance criteria:**
- Method is `protected` (accessible to subclasses, not public on the controller surface).
- Method is not `static` (it reads `ControllerBase.User`).
- Returns the first non-null claim value in priority order: `NameIdentifier` > `sub` > `oid`.
- Throws `UnauthorizedAccessException` (not `Exception`, not `InvalidOperationException`) when no claim is present.
- Exception message is exactly `"Authenticated user has no identifiable claim."`.
- A `using System.Security.Claims;` directive is added to `BaseApiController.cs`.

### FR-2: Remove the private copy from `DashboardController`
Delete the private `GetCurrentUserId()` declaration at `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs:97-105`. All five call sites (lines 38, 48, 58, 72, 86) continue to compile against the inherited method.

**Acceptance criteria:**
- The private method is removed from the file.
- The `using System.Security.Claims;` directive is removed if no other code in the file references the namespace.
- All existing call sites still compile and resolve to the inherited `BaseApiController.GetCurrentUserId()`.
- `dotnet build` succeeds.

### FR-3: Remove the private copy from `CarrierCoolingController`
Delete the private `GetCurrentUserId()` declaration at `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs:40-46`. The single call site at line 35 continues to compile against the inherited method.

**Acceptance criteria:**
- The private method is removed from the file.
- The `using System.Security.Claims;` directive is removed if no other code in the file references the namespace.
- The call site still compiles and resolves to the inherited method.
- `dotnet build` succeeds.

### FR-4: Remove the private copy from `GiftSettingsController`
Delete the private `GetCurrentUserId()` declaration at `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs:40-46`. The single call site at line 34 continues to compile against the inherited method.

**Acceptance criteria:**
- The private method is removed from the file.
- The `using System.Security.Claims;` directive is removed if no other code in the file references the namespace.
- The call site still compiles and resolves to the inherited method.
- `dotnet build` succeeds.

### FR-5: Sweep for other private duplicates
Before finalizing, search the entire `backend/src/Anela.Heblo.API/` controllers tree for any other `private string GetCurrentUserId` declaration or inline `User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.FindFirst("oid")?.Value` pattern. As of the brief (2026-05-28) only the three listed controllers contain it, but the sweep is part of the contract so the consolidation isn't half-done.

**Acceptance criteria:**
- A repository-wide grep for `GetCurrentUserId` in `backend/src/Anela.Heblo.API/Controllers/` returns only inherited call sites (no private declarations remain in any controller).
- A repository-wide grep for the exact three-step claim chain inside any controller body returns zero matches.
- `CurrentUserService.cs` (`backend/src/Anela.Heblo.API/Features/Users/CurrentUserService.cs`) is **not** modified — it serves a different consumer and intentionally returns a nullable id rather than throwing.

### FR-6: Update existing controller tests for new exception type and message
The file `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` (lines 229-333) contains four tests that exercise the per-controller `GetCurrentUserId` behavior:

- `GetCurrentUserId_WhenNoClaimsPresent_ShouldThrowException`
- `GetCurrentUserId_WhenSubClaimPresent_ShouldUseSubClaim`
- `GetCurrentUserId_WhenOidClaimPresent_ShouldUseOidClaim`
- `GetCurrentUserId_WhenMultipleClaimsPresent_ShouldPrioritizeNameIdentifier`

The "no claims" test currently asserts `Assert.ThrowsAsync<Exception>` with message `"User not found"`. After this refactor it must assert `UnauthorizedAccessException` with message `"Authenticated user has no identifiable claim."`. The three positive-path tests remain valid as-is — they verify the same priority order and continue to exercise the same code through `DashboardController`, which now inherits the method.

**Acceptance criteria:**
- `GetCurrentUserId_WhenNoClaimsPresent_ShouldThrowException` asserts on `UnauthorizedAccessException` with the new message.
- The three positive-path tests are not renamed or rewritten beyond what is required to keep them passing — they remain in `DashboardControllerTests` as integration coverage for the inherited helper.
- `dotnet test` passes for the touched test project.

### FR-7: Add direct unit coverage for the base method (only if file already exists)
If the project has a `BaseApiControllerTests` file, add four tests covering the four claim scenarios there. If no such file exists, do **not** create one — keep coverage where it lives today (in `DashboardControllerTests`). This is to avoid expanding scope into a parallel test infrastructure that isn't already established.

**Acceptance criteria:**
- If `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` already exists, four tests are added there mirroring the priority order and the throw behavior, exercised through a minimal test-only controller subclass.
- If it does not exist, no new test file is created; coverage remains in `DashboardControllerTests`.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. The method is a synchronous claim lookup with the same allocations as before. A virtual call through the base class is negligible at the controller layer.

### NFR-2: Security
- The new exception type (`UnauthorizedAccessException`) does not, by itself, produce an HTTP 401 response — ASP.NET Core will surface it as a 500 unless caught by middleware. This matches **all three** current behaviors (which throw `Exception` / `InvalidOperationException`, also surfaced as 500) and is therefore not a regression. Mapping unauthenticated callers to a proper 401 is out of scope (see Out of Scope).
- No claim values are logged. The exception message contains no PII.
- Controllers continue to be guarded by `[Authorize]` so in practice the helper is only reached for authenticated principals; the throw covers the defense-in-depth case where the principal is authenticated but missing all three id claims (e.g., a misconfigured external IdP).

### NFR-3: Backwards compatibility
- The public HTTP surface of all three controllers is unchanged.
- The thrown exception type changes from `Exception` (Dashboard) / `InvalidOperationException` (CarrierCooling, GiftSettings) to `UnauthorizedAccessException`. Anything catching the previous types specifically would no longer match — a repository-wide grep confirms no such catches exist outside the tests covered in FR-6.

### NFR-4: Style / conventions
- The new method follows the same XML-doc / comment density as the existing `Logger` and `HandleResponse<T>` members in `BaseApiController`. A short `<summary>` is acceptable; a multi-paragraph docblock is not.
- The file is run through `dotnet format` before commit (per `CLAUDE.md` validation rules).

## Data Model
No data model changes.

## API / Interface Design
No public API surface changes. The change is purely internal to the controller layer.

The inherited helper has the signature:

```csharp
// backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs
protected string GetCurrentUserId();
```

Throws `UnauthorizedAccessException` when no id claim is found.

## Dependencies
- Existing: `Microsoft.AspNetCore.Mvc.ControllerBase.User` (provides the `ClaimsPrincipal`).
- Existing: `System.Security.Claims.ClaimTypes.NameIdentifier`.
- No new NuGet packages.

## Out of Scope
- **Consolidating with `CurrentUserService`.** The DI-injected `ICurrentUserService` returns a nullable id and serves non-controller call sites; merging the two abstractions would touch unrelated features.
- **Returning a proper 401 instead of throwing.** Mapping unauthenticated/identity-less requests to an `IActionResult` (e.g., `Unauthorized()`) is a different design decision and would also require coordinating with middleware exception handling. Out of scope here; the brief explicitly asks for an `UnauthorizedAccessException` throw to match the existing pattern of throw-from-helper.
- **Refactoring `HangfireDashboardAuthorizationFilter` / `HangfireDashboardTokenAuthorizationFilter`.** These also inspect `ClaimTypes.NameIdentifier` but are filters, not controllers, and cannot inherit from `BaseApiController`.
- **Renaming or relocating `BaseApiController`.**
- **Touching the four passing positive-path tests in `DashboardControllerTests`** beyond what FR-6 strictly requires.

## Open Questions
None.

## Status: COMPLETE