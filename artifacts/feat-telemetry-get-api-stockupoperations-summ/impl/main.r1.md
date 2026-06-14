Implementation complete. Here is the output artifact:

---

# Implementation: Resolve 403 storm on `GET /api/StockUpOperations/summary`

## What was implemented

Applied the **R-A (Frontend gate)** remediation path. The React Query polling hook `useStockUpOperationsSummary` gained an optional `{ enabled?: boolean }` options bag. Both callsites (`TransportBoxList`, `GiftPackageManufacturing`) now read `usePermissionsContext()` and only enable the hook when the user holds `warehouse.stock_up.read`. The server `[FeatureAuthorize]` gate is unchanged — R-A is a UX/cost optimization that eliminates the 15-second polling storm for users without the permission. Regression coverage added as reflection-based BE tests (mirroring the existing pattern) plus Jest tests asserting the `enabled` flag at each callsite.

## Files created/modified

- `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md` — full investigation note: FR-1 route verification, FR-2 attribution (App Insights not accessible → arch-review fallback → R-A), FR-3 path selection, FR-5 single 500 (unattributable), NFR-3 post-deploy KQL
- `frontend/src/api/hooks/useStockUpOperations.ts` — added `UseStockUpOperationsSummaryOptions` interface and `options?: UseStockUpOperationsSummaryOptions` parameter forwarding `enabled` to React Query; default `true` preserves existing callers
- `frontend/src/components/pages/TransportBoxList.tsx` — added `usePermissionsContext` import; derived `canSeeStockUp`; hook and indicator now gated on permission + loading state
- `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` — same gate applied for `GiftPackageManufacture` source type
- `backend/test/Anela.Heblo.Tests/Authorization/StockUpOperationsControllerAuthorizationTests.cs` — NEW: 4 reflection-based tests asserting class-level `Warehouse_StockUp` Read, no method-level override on `GetSummary`, write actions retain Write level
- `frontend/src/api/hooks/__tests__/useStockUpOperationsSummary.test.tsx` — NEW: 2 tests for hook's `enabled` option
- `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx` — NEW: 3 tests asserting gate at TransportBoxList callsite
- `frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx` — NEW: 3 tests asserting gate at GiftPackageManufacturing callsite
- `frontend/src/components/pages/__tests__/TransportBoxList.test.tsx` — MODIFIED: added `usePermissionsContext` mock to prevent regression (pre-existing test didn't need it before Task 5)

## Tests

- **BE**: `StockUpOperationsControllerAuthorizationTests` — 4 tests PASS (reflection-based)
- **FE**: 31 tests PASS across 4 suites: hook options test (2), gate test per page (3+3), pre-existing TransportBoxList suite (23)

## How to verify

```bash
# Backend
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~StockUpOperationsControllerAuthorizationTests" --no-build

# Frontend (must use react-scripts test, not npx jest directly)
cd frontend
CI=true npx react-scripts test \
  --testPathPattern="useStockUpOperationsSummary|TransportBoxList|StockUpGate" \
  --watchAll=false --no-coverage
```

After deployment, run the KQL in `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md#NFR-3` for 3 days — `forbidden` count should drop to near-zero.

## Notes

- **App Insights not accessible** during this automated session: FR-2 caller attribution could not be queried. Applied arch-review fallback (default to R-A when attribution is impossible). The KQL queries for FR-2 and FR-5 attribution are documented in the investigation note for a human to run.
- **FR-5 (single 500)**: unattributable for the same reason — documented with the KQL to investigate.
- **`npx jest` vs `react-scripts test`**: The three new FE test files only run correctly under `CI=true react-scripts test` (CRA's Babel preset includes TypeScript). Running `npx jest` directly fails because there's no local Babel config. This is a pre-existing project constraint, not introduced by this change.
- Generated API client `frontend/src/api/generated/api-client.ts` is unchanged (NFR-4 satisfied).

## PR Summary

Stop the 209/210 403-Forbidden storm on `GET /api/StockUpOperations/summary` by gating the React Query polling hook at its two callsites on the `warehouse.stock_up.read` permission. Callers without the permission no longer fire the 15-second poll, eliminating the telemetry noise. The server `[FeatureAuthorize(Feature.Warehouse_StockUp)]` gate is untouched — R-A is a UX/cost optimization, not a security change.

Investigation (FR-2) could not attribute callers from this session (no App Insights access); arch-review fallback applied — R-A is correct regardless because it is loss-free for authorized users and eliminates the storm for unauthorized ones.

### Changes
- `frontend/src/api/hooks/useStockUpOperations.ts` — additive `enabled` option on `useStockUpOperationsSummary`
- `frontend/src/components/pages/TransportBoxList.tsx` — gate callsite on `warehouse.stock_up.read`
- `frontend/src/components/pages/GiftPackageManufacturing/index.tsx` — gate callsite on `warehouse.stock_up.read`
- `backend/test/Anela.Heblo.Tests/Authorization/StockUpOperationsControllerAuthorizationTests.cs` — reflection-based regression tests for controller authorization contract
- `frontend/src/api/hooks/__tests__/useStockUpOperationsSummary.test.tsx` — hook `enabled` option tests
- `frontend/src/components/pages/__tests__/TransportBoxList.stockUpGate.test.tsx` — permission gate tests
- `frontend/src/components/pages/GiftPackageManufacturing/__tests__/StockUpGate.test.tsx` — permission gate tests
- `docs/routines/telemetry-anomaly/2026-06-13-stockupoperations-summary-403.md` — investigation note with FR-1 through FR-5 findings and NFR-3 KQL

## Status
DONE_WITH_CONCERNS

**Concern:** FR-2 and FR-5 attribution require a human to run the documented KQL queries in App Insights. The R-A path is the correct fallback per the arch-review, but if FR-2 reveals a `should-have-access` principal, R-B should be applied instead and the investigation doc updated.