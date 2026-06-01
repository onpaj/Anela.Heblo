# Specification: Consolidate GetCurrentUserId() into BaseApiController

## Summary
Remove the duplicated `GetCurrentUserId()` private method from individual controllers and consolidate it as a single `protected` method on `BaseApiController`. This eliminates DRY violations in shared infrastructure code, standardizes the claim-fallback chain (`NameIdentifier` → `sub` → `oid`) in one place, and replaces the bare `throw new Exception` with a semantically correct `UnauthorizedAccessException`.

## Background
The same six-line user-ID extraction method appears verbatim in at least three controllers (`DashboardController`, `CarrierCoolingController`, `GiftSettingsController`). The claim fallback chain encodes a non-obvious business rule — Azure AD Entra issues the user identifier as the `oid` claim, while other identity providers use `NameIdentifier` or `sub`. If this rule changes (new IdP, claim renaming, multi-tenant adjustments), every copy must be updated independently, creating drift risk.

`BaseApiController` (`backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs`) already exists as the established place for shared controller utilities (`HandleResponse<T>`, logger access) but does not currently expose `GetCurrentUserId`. Adding it there matches the existing pattern.

A secondary defect is the use of `throw new Exception(...)` — generic exceptions cannot be cleanly mapped to a 401 response by middleware and obscure intent. `UnauthorizedAccessException` is the .NET-idiomatic choice and can be mapped to HTTP 401 by exception-handling middleware.

## Functional Requirements

### FR-1: Add GetCurrentUserId() to BaseApiController
Add a single `protected` method to `BaseApiController` that returns the authenticated user's identifier, using the existing claim fallback chain.

**Signature:**
```csharp
protected string GetCurrentUserId()
    => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
       ?? User.FindFirst("sub")?.Value
       ?? User.FindFirst("oid")?.Value
       ?? throw new UnauthorizedAccessException("Authenticated user has no identifiable claim.");
```

**Acceptance criteria:**
- Method is declared `protected` (not `private` or `public`) so derived controllers can call it but external code cannot.
- Method is non-virtual unless an existing override pattern in `BaseApiController` dictates otherwise.
- Method returns the first non-null claim value in the order: `ClaimTypes.NameIdentifier`, `sub`, `oid`.
- Method throws `UnauthorizedAccessException` (not `Exception`) when no claim is present, with the message `"Authenticated user has no identifiable claim."`.
- `using` directives for `System.Security.Claims` are present in `BaseApiController.cs`.

### FR-2: Remove duplicate implementations from all controllers
Locate every controller that currently defines its own `GetCurrentUserId()` and remove that private method, relying on the inherited method from `BaseApiController`.

**Known call sites (from the brief):**
- `DashboardController.cs:97–105`
- `CarrierCoolingController.cs:40–45`
- `GiftSettingsController.cs:40–45`

**Acceptance criteria:**
- A codebase-wide search (e.g. `grep -rn "GetCurrentUserId" backend/src --include="*.cs"`) is performed before declaring completion. Every controller-level private/protected `GetCurrentUserId()` definition is removed.
- Each affected controller inherits (directly or transitively) from `BaseApiController`. If any does not, it is changed to do so — provided no behavior regressions result (verify routing attributes, base constructors, etc.).
- All call sites of `GetCurrentUserId()` continue to compile and behave identically (same return value, same exception type from the caller's perspective — see NFR-3 below regarding the exception-type change).
- No copy of the bare `throw new Exception(...)` for this purpose remains.

### FR-3: Update HTTP behavior to return 401 on missing claim
The previous code threw `Exception` on a missing claim, which would typically surface as an HTTP 500. After this change, `UnauthorizedAccessException` should map to HTTP 401 Unauthorized.

**Acceptance criteria:**
- If the project already has middleware that maps `UnauthorizedAccessException` → 401, no further change is needed.
- If no such mapping exists, add one (in the existing global exception handler / middleware, not as per-controller try/catch) so the new exception produces a 401 response with no stack-trace leakage to the client.
- The change is documented in a code comment only if the mapping location is non-obvious; otherwise rely on the exception type for self-documentation.

### FR-4: Preserve existing behavior for the authorized path
When a user is authenticated and presents one of the three supported claims, the returned identifier must be byte-identical to what the previous per-controller implementations would have returned.

**Acceptance criteria:**
- Unit test exists asserting `NameIdentifier` is returned when present.
- Unit test exists asserting `sub` is returned when `NameIdentifier` is absent but `sub` is present.
- Unit test exists asserting `oid` is returned when both `NameIdentifier` and `sub` are absent.
- Unit test exists asserting `UnauthorizedAccessException` is thrown when all three are absent.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. Method is pure claim lookup on the existing `ClaimsPrincipal`; no I/O, no allocations beyond strings already materialized by the framework.

### NFR-2: Security
- The fallback chain order (`NameIdentifier` → `sub` → `oid`) is preserved exactly. Reordering or removing claims could allow a token with only a low-trust claim to authenticate where it previously could not. Do not reorder.
- The exception message `"Authenticated user has no identifiable claim."` does not leak sensitive data and is safe to surface in logs. It should not be returned verbatim in the HTTP response body if the project's error-handling policy hides server messages from clients — defer to existing middleware policy.
- `[Authorize]` attributes on controllers/actions are unchanged. This refactor does not relax or tighten authorization elsewhere.

### NFR-3: Backwards compatibility (exception type change)
Callers that explicitly catch `Exception` from `GetCurrentUserId()` will still catch the new `UnauthorizedAccessException` (since it derives from `Exception`). However, callers that have a narrower catch may need updating.

**Acceptance criteria:**
- A search confirms no caller has a `try { ...GetCurrentUserId()... } catch (Exception ex)` block that re-throws or maps to a non-401 result based on the exception type.
- If any such caller exists, update it to either let the exception propagate (preferred) or to catch `UnauthorizedAccessException` specifically.

### NFR-4: Test coverage
Per project standard (80%+ coverage), unit tests for the new method on `BaseApiController` are added. Existing controller tests are reviewed to ensure they still pass without modification (other than removal of tests that previously covered the per-controller private method, which can be deleted or migrated to the base-class test).

## Data Model
Not applicable. This is a pure code-organization refactor; no persisted entities are introduced or changed.

## API / Interface Design

### Public HTTP surface
Unchanged. No routes added, removed, or modified. Response shape for the authorized path is unchanged. Response status on missing claims changes from 500 → 401 (a defect fix, not a contract change).

### Internal class surface
- `BaseApiController` gains: `protected string GetCurrentUserId()`.
- `DashboardController`, `CarrierCoolingController`, `GiftSettingsController` (and any others discovered) lose their `private string GetCurrentUserId()`.

## Dependencies
- No new NuGet packages.
- Relies on `System.Security.Claims.ClaimTypes` and `Microsoft.AspNetCore.Mvc.ControllerBase.User` — both already in use.
- Relies on existing exception-handling middleware (or addition of `UnauthorizedAccessException` → 401 mapping per FR-3).

## Out of Scope
- Introducing a new `ICurrentUser`/`ICurrentUserAccessor` service abstraction for non-controller code paths (e.g. handlers, services). The brief asks only for controller-layer consolidation. If domain/application code also extracts the current user from claims, that is a separate refactor.
- Changing the claim fallback order or adding new claim types (e.g. `preferred_username`, `email`).
- Migrating the user identifier representation (still `string`).
- Adjusting `[Authorize]` policies or authentication schemes.
- Multi-tenant or impersonation concerns.

## Open Questions
None.

## Status: COMPLETE