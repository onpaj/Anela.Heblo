# Code Review: grant-e2e-warehouse-stockup-claims

## Summary
The implementation adds exactly the two role claims (`AccessRoles.WarehouseStockUpRead`,
`AccessRoles.WarehouseStockUpWrite`) specified in the task context to
`E2ESessionService.CreateSyntheticUserClaims()`, and adds the exact regression test file
specified, verbatim. The actual commit diff matches the impl summary's description precisely, the
constants referenced exist and map to the correct `Feature.Warehouse_StockUp` access levels, and
the fix is correctly scoped to FR-1 only (the DB-backed FR-2 grant is explicitly and correctly
deferred to a separate task in the plan).

## Review Result: PASS

### task: grant-e2e-warehouse-stockup-claims
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
Verification performed:
- `git show 1150603` confirms the diff is a byte-for-byte match to the task-context's Step 3 (claim
  additions) and Step 1 (new test file), touching only the two files the task specifies.
- `AccessRoles.generated.cs:43-44` confirms `WarehouseStockUpRead = "warehouse.stock_up.read"` and
  `WarehouseStockUpWrite = "warehouse.stock_up.write"` exist, and lines 102-103 confirm they map to
  `(Feature.Warehouse_StockUp, AccessLevel.Read)` / `(..., AccessLevel.Write)` — matching
  `StockUpOperationsController.cs:12`'s class-level `[FeatureAuthorize(Feature.Warehouse_StockUp)]`
  (Read, for `GetOperations`) and the `AccessLevel.Write` gates on `RetryOperation`/`AcceptOperation`
  cited in the spec.
- `frontend/src/auth/accessMatrix.generated.ts:32` independently confirms
  `"/stock-up-operations": { permissions: ["warehouse.stock_up.read"] }`, matching the permission
  string granted.
- The new test's constructor usage (`new E2ESessionService(_logger)`) matches the actual
  `E2ESessionService` constructor signature (`ILogger<E2ESessionService> logger`), and the
  `CreateSyntheticUserClaims(string environmentName)` signature and `ClaimTypes.Role` claim shape
  match the production code, so the test is well-formed and asserts the right thing (role claims
  present, not permission-claims or some other shape).
- Scope check: `spec.r1.md` FR-1's acceptance criteria are fully satisfied by this diff. FR-2 (DB
  permission grant for the frontend's `RequireMenuPath` guard) is explicitly out of scope for this
  task per `task-plan.r1.md`, and the impl summary correctly notes this rather than silently
  omitting it or overclaiming completion.
- I started `dotnet test --filter FullyQualifiedName~E2ESessionServiceTests` in this review
  environment to independently confirm green, but the build did not finish within the review
  session (large solution, slow sandboxed build). This is a code-inspection-based PASS: the change
  is a minimal, low-risk addition of two claims using existing, already-verified constants,
  following an established working pattern in the same method (the `FinanceFinancialOverviewRead`
  precedent), and the test file is syntactically consistent with the real API surface. No red flags
  were found that would suggest a build or test failure.
