All validation checks pass. The implementation is complete and committed. Let me write the output summary.

---

# Implementation: Consolidate GetCurrentUserId() into BaseApiController

## What was implemented

Removed the duplicated `GetCurrentUserId()` private method from `DashboardController`, `CarrierCoolingController`, and `GiftSettingsController`, and consolidated it as a single `protected` method on `BaseApiController`. Replaced bare `throw new Exception` / `throw new InvalidOperationException` with `UnauthorizedAccessException`. Added a new `IExceptionHandler` that maps `UnauthorizedAccessException` → HTTP 401 `ProblemDetails` with no `detail` field leaked to the client (exception logged server-side only).

## Files created/modified

- `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs` — added `using System.Security.Claims` and `protected string GetCurrentUserId()` with NameIdentifier → sub → oid fallback chain throwing `UnauthorizedAccessException` on miss
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` — removed `private string GetCurrentUserId()` block and unused `using System.Security.Claims`
- `backend/src/Anela.Heblo.API/Controllers/CarrierCoolingController.cs` — same removals
- `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` — same removals
- `backend/src/Anela.Heblo.API/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs` — new; `IExceptionHandler` returning 401 + `ProblemDetails{Status, Title, Type}`, no `detail`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — added `AddExceptionHandler<UnauthorizedAccessExceptionHandler>()` + `AddProblemDetails()`
- `backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs` — added `app.UseExceptionHandler()` before `app.UseRouting()`
- `backend/test/Anela.Heblo.Tests/Controllers/BaseApiControllerTests.cs` — new; 6 tests via inner `TestController : BaseApiController`
- `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` — deleted four obsolete `GetCurrentUserId_*` tests
- `backend/test/Anela.Heblo.Tests/Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs` — new; 3 tests

## Tests

- `BaseApiControllerTests.cs` — 6 tests: NameIdentifier priority, sub fallback, oid fallback, all-three priority, sub+oid priority, `UnauthorizedAccessException` on no claims
- `UnauthorizedAccessExceptionHandlerTests.cs` — 3 tests: 401 status + correct ProblemDetails fields, no `detail`/message leakage in body, `false` returned for non-`UnauthorizedAccessException`

**Result: 9/9 tests pass. Build: 0 errors, 158 pre-existing warnings (none introduced by this change).**

## How to verify

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BaseApiControllerTests|FullyQualifiedName~UnauthorizedAccessExceptionHandlerTests"
grep -rn "string GetCurrentUserId" backend/src --include="*.cs"   # expect: BaseApiController.cs only
grep -rn "throw new UnauthorizedAccessException" backend/src --include="*.cs"  # expect: BaseApiController.cs only
```

## Notes

- The previous agent artifact noted it could not run `dotnet` locally; build and tests have now been verified in this session — all pass.
- Minor cosmetic note from previous quality review: `ProblemDetails.Type` uses `tools.ietf.org` (from the plan spec). Both that domain and `datatracker.ietf.org` resolve; this matches what was specified in the plan and is not a correctness issue.

## PR Summary

Consolidates the duplicated `GetCurrentUserId()` method that appeared verbatim in `DashboardController`, `CarrierCoolingController`, and `GiftSettingsController` into a single `protected` method on `BaseApiController` — the established home for shared controller infrastructure alongside `HandleResponse<T>` and `Logger`.

The secondary fix upgrades the exception type from `throw new Exception("User not found")` / `throw new InvalidOperationException(...)` to `throw new UnauthorizedAccessException(...)`, paired with a new `IExceptionHandler` that maps it to a 401 ProblemDetails response. A token with no recognizable identity claim now returns 401 instead of 500. Exception message is logged server-side only; the response body contains `{status, title, type}` with no `detail`.

### Changes
- `BaseApiController.cs` — added `protected string GetCurrentUserId()` (NameIdentifier → sub → oid, `UnauthorizedAccessException` on miss)
- `DashboardController.cs`, `CarrierCoolingController.cs`, `GiftSettingsController.cs` — removed private duplicates and now-unused `using System.Security.Claims`
- `Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandler.cs` — new; maps `UnauthorizedAccessException` → 401 ProblemDetails, logs full exception server-side
- `Extensions/ServiceCollectionExtensions.cs` — registers handler + `AddProblemDetails()`
- `Extensions/ApplicationBuilderExtensions.cs` — wires `UseExceptionHandler()` before `UseRouting()`
- `Controllers/BaseApiControllerTests.cs` — new; 6-test fixture on a minimal test-derived controller
- `Infrastructure/ExceptionHandling/UnauthorizedAccessExceptionHandlerTests.cs` — new; 3-test fixture
- `Controllers/DashboardControllerTests.cs` — deleted 4 obsolete `GetCurrentUserId_*` tests (re-homed to base-class tests)

## Status
DONE