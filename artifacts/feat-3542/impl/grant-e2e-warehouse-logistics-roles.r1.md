# Implementation: grant-e2e-warehouse-logistics-roles

## What was implemented

Added `AccessRoles.WarehouseLogisticsRead` and `AccessRoles.WarehouseLogisticsWrite` role claims to the E2E synthetic user's claim set in `E2ESessionService.CreateSyntheticUserClaims`, following the exact pattern already used for `FinanceFinancialOverviewRead`. This unblocks the Transport Box pages/API for the E2E test identity, which were previously redirected (frontend `RequireMenuPath`) or 403'd (`TransportBoxController`'s `[FeatureAuthorize(Feature.Warehouse_Logistics)]`).

## Files created/modified

- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs` — added two `Claim(ClaimTypes.Role, ...)` entries (Read + Write) with an explanatory comment, right after the existing Finance role claim.
- `backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs` — new test file with 3 tests: asserts the new Warehouse_Logistics claims are present, asserts existing roles (Base, FinanceFinancialOverviewRead) are unaffected (regression guard), and asserts identity claims are unaffected.

## Tests

- `E2ESessionServiceTests.CreateSyntheticUserClaims_IncludesWarehouseLogisticsReadAndWrite` — confirmed FAILS before the fix (claim array lacked both roles), PASSES after.
- `E2ESessionServiceTests.CreateSyntheticUserClaims_StillIncludesExistingRoles_RegressionGuard` — passed throughout.
- `E2ESessionServiceTests.CreateSyntheticUserClaims_IncludesIdentityClaims` — passed throughout.
- Full `Authorization` namespace run: `Passed! - Failed: 0, Passed: 128, Skipped: 1, Total: 129` (includes pre-existing `FeatureAuthorizeAttributeTests`, `AccessMatrixTests`, `AccessMatrixJsonTests`, etc. — no regressions).
- `dotnet format Anela.Heblo.sln --verify-no-changes` on the two changed files: exit code 0, no formatting issues.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Authorization"
```

## Commit

Committed as `89048b2` on `feature/3542-E2e-Transport-Box-Pages-Fail-To-Render-Create-Rece`:

```
Grant E2E test user Warehouse_Logistics Read+Write roles

The E2E synthetic user only held Base and FinanceFinancialOverviewRead,
so every Transport Box page/API call was silently redirected (frontend
RequireMenuPath gate) or 403'd (TransportBoxController's
FeatureAuthorize(Warehouse_Logistics)). Both Read and Write are granted
because box-creation.spec.ts and box-receive.spec.ts perform real write
actions (create/open-by-code/state-change) that require the Write role.

Fixes 12 of 18 nightly transport E2E failures reported in run #191.
```

## Notes

- Did not touch `access-matrix.json`, `AccessRoles.generated.cs`, `AccessMatrix.generated.cs`, or `frontend/src/auth/accessMatrix.generated.ts` — these are auto-generated and already define the `WarehouseLogisticsRead`/`Write` constants consumed here.
- A pre-existing, unrelated build-time warning appears during `dotnet build`: the `Anela.Heblo.AccessMatrixGen` codegen tool throws a `JsonException` while regenerating `accessMatrix.generated.ts`/`AccessRoles.generated.cs` from `access-matrix.json` (exit code 134, treated as a non-fatal MSBuild warning MSB3073). This is unrelated to this change (no JSON/generated files were modified) and does not fail the build or tests — flagging for visibility only, not fixed as part of this surgical task.

## PR Summary

Granted the E2E synthetic test user the `Warehouse_Logistics` Read and Write role claims it was missing, which is the root cause of 12 of the 18 nightly Transport Box E2E failures (the remaining 6 are a separate wrong-fallback-URL bug, handled in a different task). Added a new `E2ESessionServiceTests.cs` covering the new claims plus a regression guard for the existing ones.

### Changes
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ESessionService.cs` — added `WarehouseLogisticsRead`/`Write` role claims
- `backend/test/Anela.Heblo.Tests/Authorization/E2ESessionServiceTests.cs` — new test file, 3 tests

## Status
DONE
