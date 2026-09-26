# Implementation: fix-purchasestockanalysis-validation-error

## What was implemented

`PurchaseStockAnalysisController.GetStockAnalysis` returned `BadRequest(ModelState)` when
model validation failed, exposing ASP.NET Core's raw `ModelStateDictionary` shape instead of
the project's standard `BaseResponse` error envelope. Replaced that with
`ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>()`, matching the
pattern already used by `PurchaseOrdersController`, `SuppliersController`, and
`CatalogController`. The success path (delegating to MediatR and returning `HandleResponse`)
is unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs` — added
  `using Anela.Heblo.API.Infrastructure;` and changed the invalid-`ModelState` branch to
  return `BadRequest(ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>())`.
- `backend/test/Anela.Heblo.Tests/Controllers/PurchaseStockAnalysisControllerTests.cs`
  (new) — unit tests covering both the invalid-`ModelState` path (asserts the response body is
  a `GetPurchaseStockAnalysisResponse` with `Success == false` and
  `ErrorCode == ErrorCodes.ValidationError`, and that MediatR is never invoked) and the
  valid-`ModelState` path (asserts MediatR's response flows through unchanged via `OkObjectResult`).

## Tests

- `PurchaseStockAnalysisControllerTests.GetStockAnalysis_InvalidModelState_ReturnsStandardizedValidationError` — PASS
- `PurchaseStockAnalysisControllerTests.GetStockAnalysis_ValidModelState_DelegatesToMediator` — PASS

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseStockAnalysisControllerTests"
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

## Notes

- The task context's Step 5 referenced `backend/Anela.Heblo.sln`; the solution file actually
  lives at the repo root (`Anela.Heblo.sln`), so the build/format commands above use the
  correct path.
- Full-suite run: 7161 passed, 4 skipped, 110 failed. Every one of the 110 failures is a
  pre-existing repository/SQL-shape/integration test that requires Docker via Testcontainers
  (`System.ArgumentException: Docker is either not running or misconfigured...`) — this sandbox
  has no Docker daemon. None of the failures are in `Controllers/PurchaseStockAnalysisControllerTests`,
  `GetPurchaseStockAnalysisHandlerTests`, or `GetPurchaseStockAnalysisHandlerDiacriticsTests`, and
  none relate to this task's change. `dotnet format --verify-no-changes` and `dotnet build`
  (0 errors, only pre-existing warnings) both pass clean.

## PR Summary
Standardized `PurchaseStockAnalysisController.GetStockAnalysis`'s validation-error response to use `ErrorResponseHelper.CreateValidationError<GetPurchaseStockAnalysisResponse>()` instead of returning the raw `ModelState`, matching the pattern already used by `PurchaseOrdersController` and other controllers. Added unit tests covering both the invalid- and valid-model-state paths.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/PurchaseStockAnalysisController.cs` — standardized validation error response
- `backend/test/Anela.Heblo.Tests/Controllers/PurchaseStockAnalysisControllerTests.cs` — new unit tests

## Status
DONE
