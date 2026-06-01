Both reviewers passed cleanly. One minor note from the quality reviewer: the `ProblemDetails.Type` URL uses `tools.ietf.org` which still resolves, but `datatracker.ietf.org` is the more current canonical domain — this is cosmetic and not a correctness issue.

Here is the implementation artifact:

---

# Implementation: Consolidate GetCurrentUserId() into BaseApiController

## What was implemented

Removed the duplicated `GetCurrentUserId()` private method from three controllers and consolidated it as a single `protected` method on `BaseApiController`. Replaced bare `throw new Exception` / `throw new InvalidOperationException` with `UnauthorizedAccessException`. Added a new `IExceptionHandler` that maps `UnauthorizedAccessException` → HTTP 401 `ProblemDetails` (no `detail` field leaked to client, full exception logged server-side).

## Files created/modified

- `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` — added `using System.Security.Claims` and `protected string GetCurrentUserId()` with NameIdentifier → sub → oid fallback chain
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` — removed `private string GetCurrentUserId()` block and unused `using System.Security.Claims`
- `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` — same removals
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` — same removals
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs` — new; `IExceptionHandler` returning 401 + `ProblemDetails{Status,Title,Type}`, no `detail`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — added `AddExceptionHandler<UnauthorizedAccessExceptionHandler>()` + `AddProblemDetails()` inside `AddCrossCuttingServices`
- `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` — added `app.UseExceptionHandler()` before `app.UseRouting()`
- `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` — new; 6 tests via inner `TestController : BaseApiController`
- `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` — deleted four `GetCurrentUserId_*` tests (lines 229–333)
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs` — new; 3 tests: 401 shape, no message leak, non-matching exception passthrough

## Tests

- `BaseApiControllerTests.cs` — 6 tests covering NameIdentifier priority, sub fallback, oid fallback, all-three priority, sub+oid priority, and `UnauthorizedAccessException` on no claims
- `UnauthorizedAccessExceptionHandlerTests.cs` — 3 tests: 401 status + correct ProblemDetails fields, no `detail` / no message in body, `false` returned for non-`UnauthorizedAccessException`

## How to verify

```bash
# Build
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj

# Run new tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BaseApiControllerTests|FullyQualifiedName~UnauthorizedAccessExceptionHandlerTests"

# Full suite
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj

# Sweep checks
grep -rn "private string GetCurrentUserId" backend/src --include="*.cs"   # expect: no output
grep -rn "protected string GetCurrentUserId" backend/src --include="*.cs" # expect: BaseApiController.cs only
grep -rn "throw new Exception" backend/src --include="*.cs"               # expect: no "User not found" match
```

## Notes

- `dotnet` is not available in this worktree shell so build/test could not be run locally. All changes are mechanically derived from exact code blocks in the plan; both spec-compliance and code-quality reviewers returned PASS on all items.
- Minor: `ProblemDetails.Type` uses `tools.ietf.org` (from the plan). The `datatracker.ietf.org` domain is the more current canonical URL for RFC references, but both resolve and this is cosmetic.

## PR Summary

Consolidates the duplicated `GetCurrentUserId()` method that appeared verbatim in `DashboardController`, `CarrierCoolingController`, and `GiftSettingsController` into a single `protected` method on `BaseApiController`. This is the established home for shared controller infrastructure (alongside `HandleResponse<T>` and `Logger`).

The secondary fix upgrades the exception type from `throw new Exception("User not found")` / `throw new InvalidOperationException(...)` to `throw new UnauthorizedAccessException(...)`, and pairs it with a new `IExceptionHandler` that maps that exception to a 401 ProblemDetails response — so a token with no recognizable identity claim now returns 401 instead of 500.

### Changes
- `BaseApiController.cs` — added `protected string GetCurrentUserId()` (NameIdentifier → sub → oid, `UnauthorizedAccessException` on miss)
- `DashboardController.cs`, `CarrierCoolingController.cs`, `GiftSettingsController.cs` — removed private duplicates and now-unused `using System.Security.Claims`
- `Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs` — new; maps `UnauthorizedAccessException` → 401 ProblemDetails, logs full exception server-side, no `detail` in body
- `Extensions/ServiceCollectionExtensions.cs` — registers handler + `AddProblemDetails()`
- `Extensions/ApplicationBuilderExtensions.cs` — wires `UseExceptionHandler()` before `UseRouting()`
- `Controllers/BaseApiControllerTests.cs` — new; 6-test fixture on a minimal test-derived controller
- `Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs` — new; 3-test fixture
- `Controllers/DashboardControllerTests.cs` — deleted 4 obsolete `GetCurrentUserId_*` tests (behavior re-homed to base-class tests)

## Status
DONE