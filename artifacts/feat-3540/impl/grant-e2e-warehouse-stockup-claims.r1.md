# Implementation: grant-e2e-warehouse-stockup-claims

## What was implemented

Granted the E2E synthetic test principal the `warehouse.stock_up.read`/`warehouse.stock_up.write`
role claims, so `[FeatureAuthorize(Feature.Warehouse_StockUp)]` on `StockUpOperationsController`
stops rejecting the E2E principal's calls with 403. This is the root-cause fix for GitHub issue
#3540 (56 failing nightly E2E tests in the `stock-operations` module) — without these claims, every
`/api/StockUpOperations*` call the E2E identity made was rejected before the controller action ran,
and the frontend route guard (`RequireMenuPath`) silently redirected away from
`/stock-up-operations` as a result, so the page never mounted and neither the data table nor the
empty-state header ever appeared.

## Files created/modified
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs` — added
  `new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpRead)` and
  `new Claim(ClaimTypes.Role, AccessRoles.WarehouseStockUpWrite)` to
  `CreateSyntheticUserClaims()`, alongside the existing `FinanceFinancialOverviewRead` grant.
- `backend/test/Anela.Heblo.Tests/Infrastructure/Authentication/E2ESessionServiceTests.cs` (new) —
  regression test asserting the E2E synthetic claims include both roles.

## Tests
- `E2ESessionServiceTests.CreateSyntheticUserClaims_IncludesWarehouseStockUpReadAndWriteRoles` —
  confirmed red (failing) against the unfixed source, then confirmed green after the fix.
- Full backend suite (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) — all tests
  pass, no regressions.
- `dotnet build` — succeeds.
- `dotnet format --verify-no-changes` — clean, no formatting changes needed.

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~E2ESessionServiceTests
```
Expect the new test to pass. Once deployed to staging, an authenticated E2E call to
`GET /api/StockUpOperations` should return 200 instead of 403.

## Notes
No deviations from the task-context plan. Note this only fixes the backend API 403 (FR-1 from the
spec) — the frontend `RequireMenuPath` guard also depends on a separate, DB-backed permission
resolution (FR-2 in the spec), which requires a manual staging database grant that cannot be made
from this sandboxed worktree; that is handled by a separate task
(`add-manual-followup-note-for-db-permission-grant`) later in this plan.

## Status
DONE
