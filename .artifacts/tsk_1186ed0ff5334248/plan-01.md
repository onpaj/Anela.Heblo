# Plan: Route Packaging (Baleni) FE hooks through the generated API client

## Summary

Eight Packaging frontend hooks bypass the OpenAPI-generated `api-client.ts` and either reach into the client's private transport (`apiClient.http.fetch` / `.baseUrl` via `as unknown as` / `as any`) or call `getAuthenticatedFetch()` against a hand-built URL string. All eight hand-declare DTO interfaces that the generator already produces. This plan rewrites those hooks to call the generated `packaging_*` client methods and the generated DTOs, matching the pattern already used by `usePackingMaterials.ts` (fixed under #3221 for the sibling PackingMaterials module).

## Context

`CLAUDE.md` states the OpenAPI TypeScript client is auto-generated on build and is the single source of truth for the backend contract. Reaching around it via `apiClient.http`/`.baseUrl` or raw `fetch` re-declares that contract by hand. When a backend DTO changes, the generated client updates automatically but these hooks silently keep compiling against the stale hand-written shape — the drift only surfaces as a runtime `undefined` or parse failure in the packing kiosk, not a compile error. `as any` on parsed JSON additionally disables all response type-checking. This is an established, already-accepted arch-review class (#3221 CLOSED for PackingMaterials; open equivalents #3833, #3797, #3730 for other modules). This ticket only records the finding — a **separate** downstream task performs the fix; this plan produces the requirements/spec for that task.

## Scope — files confirmed in violation

Verified directly against the current tree (not just the filed finding):

1. `frontend/src/api/hooks/useScanPackingOrder.ts` — `apiClient.http.fetch` (line 83) via `ApiClientWithInternals` cast (line 82); hand-declared `Cooling`, `PackingOrderItem`, `PackingEligibility`, `ShippingAddress`, `PackingOrder`, `ScanShipmentPackage`, `ScanShipment`, `ScanPackingOrderResult` (lines 10–59); `as any` on parsed JSON (line 92). → generated `packaging_ScanOrder` / `ScanPackingOrderResponse` (api-client.ts:8798, :33567).
2. `frontend/src/api/hooks/useResetOrderShipment.ts` — same `.http.fetch` pattern (line 31); imports hand-written `ScanShipment` type from hook #1. → generated `packaging_ResetShipment` / `ResetOrderShipmentResponse` (api-client.ts:8843, :33948).
3. `frontend/src/api/hooks/useCompletePackingOrder.ts` — same `.http.fetch` pattern (line 17). → generated `packaging_CompletePacking` / `CompletePackingOrderResponse` (api-client.ts:9207, :34876).
4. `frontend/src/api/hooks/usePackages.ts` — `usePackagesQuery` (line 69) **and** `useDeletePackageMutation` (line 93) both use `apiClient.http.fetch` via `as any`; hand-declared `PackageDto`, `GetPackagesRequest`, `GetPackagesResponse` (lines 4–35). → generated `packaging_GetPackages` / `GetPackagesResponse` (api-client.ts:9074, :34637) and `packaging_DeletePackage` / `DeletePackageResponse` (api-client.ts:9170, :34843). Note: `useDeletePackageMutation` was not named in the filed finding but is the same violation in the same file — in scope.
5. `frontend/src/api/hooks/usePackingDashboard.ts` — raw `getAuthenticatedFetch()` against a hand-built URL (lines 26–27); hand-declared `PackerStatsDto`, `GetPackingDashboardResponse` (lines 4–16). → generated `packaging_GetDashboard` / `GetPackingDashboardResponse` (api-client.ts:9002, :34151).
6. `frontend/src/api/hooks/usePackingStatistics.ts` — same raw-fetch pattern (lines 80–81); hand-declared `PackingStatisticsSummary`, `DailyThroughput`, `HourBucket`, `PackerThroughput`, `CarrierMix`, `PackagesPerOrderBucket`, `PackingStatisticsResponse`, `PackingStatisticsParams` (lines 4–63). → generated `packaging_GetStatistics` / `GetPackingStatisticsResponse` (api-client.ts:9036, :34252).
7. `frontend/src/api/hooks/useOrderTrackingNumber.ts` — same `.http.fetch` pattern (line 12); not named in the finding's bullet list but referenced via the finding's generated-endpoint citation (api-client.ts:8928). → generated `packaging_GetOrderTrackingNumber` / `GetOrderTrackingNumberResponse`.
8. `frontend/src/api/hooks/useOrderTrackingNumbers.ts` — same pattern; → generated `packaging_GetOrderTrackingNumbers` / `GetOrderTrackingNumbersResponse` (api-client.ts:8965).

Reference implementation already in the codebase for this exact rework: `frontend/src/api/hooks/usePackingMaterials.ts` (imports generated request/response types, re-exports them, calls `getAuthenticatedApiClient().packingMaterials_*` directly — no manual `fetch`, no hand-declared DTOs).

## Functional requirements

**FR-1 — Replace manual transport with generated client calls.**
Every hook above must call its corresponding `getAuthenticatedApiClient().packaging_*` method instead of `apiClient.http.fetch(...)` / `getAuthenticatedFetch()(...)`. No hook may reference `apiClient.http`, `apiClient.baseUrl`, or the local `ApiClientWithInternals` cast after the change.
*Acceptance:* `grep -rn "\.http\.fetch\|ApiClientWithInternals\|getAuthenticatedFetch" frontend/src/api/hooks/useScanPackingOrder.ts frontend/src/api/hooks/useResetOrderShipment.ts frontend/src/api/hooks/useCompletePackingOrder.ts frontend/src/api/hooks/usePackages.ts frontend/src/api/hooks/usePackingDashboard.ts frontend/src/api/hooks/usePackingStatistics.ts frontend/src/api/hooks/useOrderTrackingNumber.ts frontend/src/api/hooks/useOrderTrackingNumbers.ts` returns no matches.

**FR-2 — Delete hand-declared DTOs; use generated types.**
Remove the hand-written interfaces/types listed per file above. Where a consumer component needs the shape, import the generated type (directly, or re-export it from the hook module the way `usePackingMaterials.ts` does) rather than redeclaring it.
*Acceptance:* none of the removed interface names remain declared in the hook files; `tsc`/`npm run build` passes, proving every consumer still resolves a compatible type from the generated client.

**FR-3 — Preserve existing business-error handling.**
The generated `packaging_*` responses extend `BaseResponse` (`success?`, `errorCode?: ErrorCodes`), the same envelope these hooks currently hand-parse from raw JSON (`data.success` / `data.errorCode`). Keep the existing `SCAN_ERROR_MESSAGES` / `RESET_ERROR_MESSAGES` / `COMPLETE_ERROR_MESSAGES` Czech message maps and the "throw `Error(message)` when `!success`" behavior, but read `success`/`errorCode` off the typed generated response instead of an `any`-cast JSON blob. `errorCode` on the generated type is the `ErrorCodes` enum, not a bare string — the message-map lookup keys must line up with that enum (or keep `as string` indexing if the enum is a superset, but do not silently drop the existing Czech copy).
*Acceptance:* existing tests in `frontend/src/api/hooks/__tests__/useCompletePackingOrder.test.ts` and component tests under `frontend/src/components/baleni/**/__tests__/*` continue to pass unmodified in behavior (error toasts/messages unchanged), only mocks updated to match the new call surface.

**FR-4 — Reconcile `Date` vs `string` field types.**
Generated DTOs type date/time fields as `Date` objects (e.g. `GetPackingDashboardResponse.ordersBeingPackedCountLastSync`, `PackageDto.packedAt`, `DailyThroughputDto.date`, `GetPackingStatisticsResponse.fromDate/toDate`), where the current hand-written types use `string`. Any consumer component doing string formatting/comparison on these fields must be updated to handle `Date` (or the hook must explicitly convert `.toISOString()`/format at the boundary, documented as a deliberate choice, not left inconsistent).
*Acceptance:* no consumer component calls string methods (`.slice`, `.split`, template-literal date math) directly on a field that is now typed `Date`; `tsc` catches any mismatch, and all mismatches are resolved (not suppressed with `as any`).

**FR-5 — `usePackagesQuery` request parameter mapping.**
`packaging_GetPackages` takes positional typed parameters (`orderCode, customerName, packageNumber, carrier: Carriers | null, fromDate: Date | null, toDate: Date | null, pageNumber, pageSize, sortBy, sortDescending`) rather than a hand-built `URLSearchParams` query string. `GetPackagesRequest.carrier` is currently a bare `string`; the generated method expects the `Carriers` enum — confirm/convert the value at the call site.
*Acceptance:* `usePackagesQuery` compiles against the generated method signature with no `as any`; carrier filtering in `ZasilkyPage.tsx` still round-trips correctly (verified via existing `ZasilkyPage.test.tsx`).

**FR-6 — No regression in kiosk scan/reset/complete flows.**
`useScanPackingOrder`, `useResetOrderShipment`, and `useCompletePackingOrder` back the packing-kiosk hot path (`BaleniPacking.tsx`, `PackingShipmentCreator.tsx`, `PackingShipmentDoneView.tsx`). These must keep working identically from the UI's perspective — same mutation signatures (`orderCode`, `numberOfPackages`, `packingUserId`), same return shapes consumed by sub-components.
*Acceptance:* `frontend/src/components/baleni/__tests__/BaleniPacking.test.tsx`, `PackingShipmentCreator.test.tsx`, `PackingShipmentDoneView.test.tsx`, `PackingItems.test.tsx`, `PackingLabelPrinter.test.tsx`, `PackingLabelPrintModal.test.tsx` all pass.

## Non-functional requirements

- **Type safety:** zero new `as any` / `as unknown as` in the touched files; the point of the change is to restore compile-time checking against the real contract.
- **No behavior change for end users:** this is a refactor of the transport layer only — request/response payloads, URLs, and HTTP methods must remain byte-identical to what the backend already receives/returns (the generated client hits the same routes).
- **No backend changes.** This is FE-only; backend `packaging_*` endpoints and DTOs already exist and are unchanged.

## Data model

No new data model — this is a consumption-layer change. The relevant "entities" are the generated DTOs already produced by the OpenAPI generator (`ScanOrderData`, `ScanShipmentData`, `ScanPackingOrderItemDto`, `ScanOrderEligibility`, `ShippingAddress`, `ResetShipmentData`, `PackerStatsDto`, `PackingStatisticsSummaryDto`, `DailyThroughputDto`, `HourBucketDto`, `PackerThroughputDto`, `CarrierMixDto`, `PackagesPerOrderBucketDto`, `PackageDto`, `BaseResponse` with `success`/`errorCode: ErrorCodes`). Verified field-for-field parity with the current hand-written interfaces (names and nesting match); the only structural difference found is `Date` vs `string` typing on timestamp fields (see FR-4).

## Interfaces

No new endpoints. Existing backend routes consumed via generated client methods instead of raw fetch:
- `POST /api/packaging/orders/{orderCode}/scan` → `packaging_ScanOrder`
- `POST /api/packaging/orders/{orderCode}/shipment/reset` → `packaging_ResetShipment`
- `POST /api/packaging/orders/{orderCode}/packing/complete` → `packaging_CompletePacking`
- `GET /api/packaging/packages` → `packaging_GetPackages`
- `DELETE /api/packaging/packages/{id}` → `packaging_DeletePackage`
- `GET /api/packaging/dashboard` → `packaging_GetDashboard`
- `GET /api/packaging/statistics` → `packaging_GetStatistics`
- `GET /api/packaging/orders/{orderCode}/tracking-number` → `packaging_GetOrderTrackingNumber`
- `GET /api/packaging/orders/{orderCode}/tracking-numbers` → `packaging_GetOrderTrackingNumbers`

## Dependencies and scope

**Depends on:** the generated `api-client.ts` already containing all nine `packaging_*` methods and their DTOs (confirmed present, no backend/codegen work needed).

**In scope:** the 8 hook files listed above, plus any consumer component that breaks compilation because a hand-written type it imported no longer exists or a field changed shape (expected candidates: `ZasilkyPage.tsx`, `BaleniStatistics.tsx`, `PackingCharts.tsx`, `PackingHourHeatmap.tsx`, `BaleniHome.tsx`, `PackingShipmentCreator.tsx`, `PackingShipmentDoneView.tsx`, `PackingLabelPrinter.tsx`, `PackingItems.tsx`, `PackingOrderMeta.tsx`, `PackingCoolingIndicator.tsx`, `PackingStateWarning.tsx`, `BaleniPacking.tsx` — all listed as current consumers of the affected hooks/types).

**Out of scope:**
- Backend changes of any kind.
- Any other module's arch-review findings (#3833 KnowledgeBase, #3797/#3730 Manufacture) — separate tickets.
- Behavioral/UX changes beyond what's forced by the type change (e.g. do not redesign error handling, do not add new fields to the UI even though the generated DTOs may expose more than the hand-written ones did).
- Actually performing the fix — this plan is input to a downstream implementation task.

## Rough plan (for the downstream fix task)

1. Rework `useScanPackingOrder.ts`: replace manual fetch with `packaging_ScanOrder`, delete hand-declared interfaces, re-export needed generated types (`Cooling`, item/eligibility/address/shipment types) for consumers that currently import them from this file.
2. Rework `useResetOrderShipment.ts` and `useCompletePackingOrder.ts` the same way, updating their imports of `ScanShipment` to the generated equivalent.
3. Rework `usePackages.ts`: both `usePackagesQuery` and `useDeletePackageMutation` onto `packaging_GetPackages` / `packaging_DeletePackage`; resolve the `carrier: string` → `Carriers` enum mapping.
4. Rework `usePackingDashboard.ts` and `usePackingStatistics.ts` onto `packaging_GetDashboard` / `packaging_GetStatistics`.
5. Rework `useOrderTrackingNumber.ts` and `useOrderTrackingNumbers.ts` onto `packaging_GetOrderTrackingNumber(s)`.
6. Fix up every consumer component/test that breaks on `tsc`/`npm run build` due to removed hand-written types or `Date`-vs-`string` field changes.
7. Update/adjust mocks in the affected `__tests__` files to mock the generated client method instead of `fetch`/`apiClient.http`.
8. Run `npm run build`, `npm run lint`, and the full FE test suite for the `baleni`/`packaging` scope; run `dotnet build` + `dotnet format` if any BE file is touched (expected: none).
9. Do not run the nightly E2E suite as part of this fix (per project convention: E2E runs nightly, not in PR CI) but note in the PR description that `frontend/test/e2e/` packaging specs, if any, should be spot-checked.

## Open questions

- **Carrier enum mapping (FR-5):** `GetPackagesRequest.carrier` is currently a free `string` from a filter UI. Need to confirm the filter UI already only emits values matching the `Carriers` enum, or whether a mapping/cast is needed. Default assumption: the UI's carrier filter values already match `Carriers` enum members (same source of truth), so a straight cast is safe — verify against `ZasilkyPage.tsx`'s carrier filter control during implementation.
- **`errorCode` typing (FR-3):** hand-written error-message maps key off bare strings (e.g. `'ShoptetOrderNotFound'`). Need to confirm these exact strings exist as `ErrorCodes` enum members, or whether they're endpoint-specific string literals not in the shared enum — if the latter, the generated `errorCode` field type may not literally match, and the map lookup should keep treating it as a string via the enum's string-backed values (TS string enums compare fine against their literal values, so no code-level ambiguity is expected, but worth confirming no enum member is missing for these specific packaging-domain codes).
- **`Date` field handling (FR-4):** default is to update consumers to work with real `Date` objects (e.g. `date.toLocaleDateString('cs-CZ')`) rather than reformatting hooks to coerce back to `string`, since that best matches the pattern generated clients establish elsewhere in the codebase. If a consumer relies on exact ISO-string formatting for a chart library key, converting at the point of use (not in the hook) is preferred so the hook keeps returning the generated shape unmodified.
- **Re-export vs. inline import for consumers:** `usePackingMaterials.ts` re-exports the generated types it uses so consumers don't import `../generated/api-client` directly. Follow the same convention for the reworked packaging hooks for consistency, unless a consumer already imports directly from `generated/api-client` elsewhere in the module (grep during implementation to confirm the prevailing convention).
