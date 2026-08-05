# Design: Route Packaging (Baleni) FE hooks through the generated API client

No UX/UI section — this is a transport-layer refactor only. No screens, wireframes, or interaction flows change. Every consumer component keeps its current markup and behavior; only the data types it receives change shape at the margins (see "Consumer impact" below).

## Component design

Each hook keeps its current public shape (hook name, `useQuery`/`useMutation` signature, return type consumed by components) so calling components need no restructuring — only their type imports and any `Date`-vs-`string` handling change. Internally, each hook's `queryFn`/`mutationFn` swaps its manual HTTP call for one generated `packaging_*` client method, and the hand-declared DTO interfaces are deleted in favour of the generated ones (imported and re-exported the way `usePackingMaterials.ts` already does — see `frontend/src/api/hooks/usePackingMaterials.ts:3-41`).

Verified against `frontend/src/api/generated/api-client.ts`: every generated `packaging_*` response DTO is field-for-field identical to today's hand-written interface (same names, same nesting) except that generated date/time fields are typed `Date`, not `string`. No backend or codegen change is needed.

### 1. `useScanPackingOrder.ts`

- **Responsibility:** unchanged — scan an order code into the kiosk, returning the order + shipment (or creating one).
- **Transport:** `getAuthenticatedApiClient().packaging_ScanOrder(orderCode, numberOfPackages, body)` replaces the `apiClient.http.fetch` + manual JSON parse (api-client.ts:8798).
  - `body` is a plain object `{ packingUserId }` cast `as ScanOrderBody` (same casting convention `usePackingMaterials.ts:80` uses for its create/update bodies) — no need to `new ScanOrderBody(...)`, the generated client only calls `JSON.stringify(body)` on it.
- **Types deleted:** `Cooling`, `PackingOrderItem`, `PackingEligibility`, `ShippingAddress`, `PackingOrder`, `ScanShipmentPackage`, `ScanShipment`, `ScanPackingOrderResult`, the local `ApiClientWithInternals` interface.
- **Types replacing them (re-exported from this file for consumers):**
  | Deleted | Generated replacement |
  |---|---|
  | `Cooling` | `Cooling` (generated enum `'None' \| 'L1' \| 'L2'`, identical values) |
  | `PackingOrderItem` | `ScanPackingOrderItemDto` |
  | `PackingEligibility` | `ScanOrderEligibility` |
  | `ShippingAddress` | `ShippingAddress` (name unchanged, now the generated class) |
  | `PackingOrder` | `ScanOrderData` |
  | `ScanShipmentPackage` | `ScanShipmentPackage` (name unchanged, generated class) |
  | `ScanShipment` | `ScanShipmentData` |
  | `ScanPackingOrderResult` | `ScanPackingOrderResponse` (has `.order`/`.shipment`, both optional — same as before) |
  - Field parity confirmed directly against api-client.ts:33604-33910 — no field is renamed, added, or removed; no `Date` fields in this DTO tree, so **no consumer changes are required** for scan/reset/complete beyond the type import swap.
- **Error handling:** `SCAN_ERROR_MESSAGES` map and "throw on `!success`" behavior unchanged, but read `success`/`errorCode` straight off the typed `ScanPackingOrderResponse` (extends `BaseResponse` — api-client.ts:13228 — `success?: boolean`, `errorCode?: ErrorCodes`). All five keys used in `SCAN_ERROR_MESSAGES` (`ShoptetOrderNotFound`, `ShipmentCarrierNotResolved`, `ShipmentCreationFailed`, `ShipmentOrderWeightUnavailable`, `PackingUserNotEligible`) are confirmed present as `ErrorCodes` enum members (api-client.ts:13692,13750-13762). A string enum member widens to `string` on read, so `SCAN_ERROR_MESSAGES[data.errorCode]` type-checks with **no `as string` cast** — this resolves the plan's open question about `errorCode` typing.
- **Consumers (`BaleniPacking.tsx`, `PackingOrderMeta.tsx`, `PackingCoolingIndicator.tsx`, `PackingStateWarning.tsx`, `PackingItems.tsx`, `PackingShipmentCreator.tsx`) keep importing `PackingOrder`/`ScanShipment` by name** — re-export them as type aliases (`export type PackingOrder = ScanOrderData; export type ScanShipment = ScanShipmentData;`) from this file rather than forcing a rename across six consumer files. This is the one place this plan deviates from "just re-export the generated name" — renaming imports in six files is unnecessary churn when a type alias does the same job.

### 2. `useResetOrderShipment.ts`

- Same rework: `getAuthenticatedApiClient().packaging_ResetShipment(orderCode, numberOfPackages)` (api-client.ts:8843) replaces the manual fetch.
- Return type becomes `ResetShipmentData` (api-client.ts:33981) instead of the imported `ScanShipment` alias — field-identical (`shipmentGuid`, `packages: ResetShipmentPackage[]`, `pendingCompletion`), but it is a **structurally distinct generated class** from `ScanShipmentData`, not the same type. `useResetOrderShipment`'s public return type changes from `ScanShipment` to `ResetShipmentData`; since both shapes are identical field-for-field, `PackingShipmentCreator.tsx` (the only consumer that receives a value from this hook and threads it interchangeably with `useScanPackingOrder`'s shipment) needs its prop type widened to accept either (`ScanShipmentData | ResetShipmentData`, or a local structural union) — verify at implementation time whether it already treats the two as separate props or a shared one.
- `RESET_ERROR_MESSAGES` keys (`NoShipmentToReset`, `InvalidPackageCount`, `ShipmentCancelFailed`, `ShipmentCreationFailed`, `ShipmentCarrierNotResolved`, `ShipmentOrderWeightUnavailable`) all confirmed in `ErrorCodes` (api-client.ts:13750-13761). Same no-cast indexing as above.

### 3. `useCompletePackingOrder.ts`

- `getAuthenticatedApiClient().packaging_CompletePacking(orderCode)` (api-client.ts:9207) replaces the manual fetch. Response is `CompletePackingOrderResponse { completed?: boolean }` (api-client.ts:34876) — the hook still returns `void` to its mutation (the `completed` flag was never surfaced to callers).
- `COMPLETE_ERROR_MESSAGES['PackingCompletionFailed']` confirmed in `ErrorCodes` (api-client.ts:13761).
- Existing test `useCompletePackingOrder.test.ts` currently mocks `getAuthenticatedApiClient` to return `{ baseUrl, http: { fetch: mockFetch } }` and asserts the exact URL/method passed to `fetch`. It must be rewritten to mock `getAuthenticatedApiClient` returning `{ packaging_CompletePacking: mockFn }` and assert `mockFn` was called with `'25/0001'` — the assertion moves from "was the right raw URL constructed" (now the generated client's job, already covered by its own generation/tests) to "was the right generated method called with the right argument." This is the mocking-pattern shift that applies to every hook test in scope.

### 4. `usePackages.ts` (`usePackagesQuery` + `useDeletePackageMutation`)

- `usePackagesQuery`: `getAuthenticatedApiClient().packaging_GetPackages(orderCode, customerName, packageNumber, carrier, fromDate, toDate, pageNumber, pageSize, sortBy, sortDescending)` (api-client.ts:9074) replaces manual `URLSearchParams` + fetch. Positional params replace the query-string builder; `undefined`/`null` handling is the generated client's job now (it already `if (x !== undefined && x !== null)`-guards each param).
- `useDeletePackageMutation`: `getAuthenticatedApiClient().packaging_DeletePackage(id)` (api-client.ts:9170) replaces the manual `DELETE` fetch. Response is `DeletePackageResponse { deleted?: boolean }` — mutation can keep returning `void`/ignore the flag, matching current behavior (current code just returns the raw parsed JSON and nothing reads it beyond invalidating the query).
- **`GetPackagesRequest.carrier` type:** change from `carrier?: string` to `carrier?: Carriers`. Verified safe: `ZasilkyFilters.tsx:2,71` already builds its `<select>` options by iterating `CARRIER_LABELS` keyed by the `Carriers` enum (`Object.entries(CARRIER_LABELS) as [Carriers, string][]`), so every non-empty value `ZasilkyPage.tsx`'s `filters.carrier` can hold is already a `Carriers` member — the only other value is `""` (unselected), which maps to `undefined` via `filters.carrier || undefined` exactly as today. This resolves the plan's carrier-mapping open question with no UI change needed. `ZasilkyPage.tsx`'s `FilterValues.carrier` itself stays `string` (it's raw `<select>` state); the cast to `Carriers` happens once, in `usePackagesQuery`'s request-building, at the hook boundary — not in the filter component.
- **`GetPackagesRequest.fromDate`/`toDate`:** stay `string` (`YYYY-MM-DD`, as produced by `ZasilkyFilters`'s `<input type="date">`) at the `GetPackagesRequest` boundary — convert to `Date` inside `usePackagesQuery`'s `queryFn` immediately before the generated call (`request.fromDate ? new Date(request.fromDate) : undefined`). Keeping the hook's *request* type on plain strings avoids touching `ZasilkyPage.tsx`/`ZasilkyFilters.tsx` at all; only the internal call to the generated method needs the `Date` conversion.
- **`PackageDto`:** deleted; use generated `PackageDto` (api-client.ts:34690) directly — identical fields except `packedAt: Date` (was `string`). `ZasilkyTable.tsx:63` currently does `new Date(p.packedAt).toLocaleString("cs-CZ")` — the `Date` constructor accepts a `Date` argument as-is, so this line keeps compiling and behaving identically without modification (though it can be simplified to `p.packedAt.toLocaleString("cs-CZ")` — cosmetic, not required).
- **`GetPackagesResponse`:** deleted; generated `GetPackagesResponse` (api-client.ts:34637) has the same `items`/`totalCount`/`pageNumber`/`pageSize` fields, now typed `PackageDto[]` (generated) instead of the local `PackageDto[]`. `ZasilkyPage.tsx` and `ZasilkyTable.tsx` only destructure these fields by name — no consumer change beyond the import source.

### 5. `usePackingDashboard.ts`

- `getAuthenticatedApiClient().packaging_GetDashboard()` (api-client.ts:9002) replaces `getAuthenticatedFetch()` against a hand-built URL.
- `GetPackingDashboardResponse` deleted in favour of the generated class (api-client.ts:34151) — same fields, except `ordersBeingPackedCountLastSync: Date | undefined` (was `string | null`).
- **Consumer impact — `BaleniHome.tsx:43`:** `StatCard`'s `syncTime?: string | null` prop must widen to `syncTime?: Date | null` (or `Date | undefined`, matching the generated field's actual optionality — it's `Date | undefined`, not `| null`). `BaleniHome.tsx:54`'s `new Date(syncTime).toLocaleTimeString(...)` keeps compiling and behaving identically (Date constructor accepts a Date), so only the prop type annotation needs updating, not the render logic.
- `PackerStatsDto` deleted in favour of generated `PackerStatsDto` (api-client.ts:34208) — identical fields (`packerId`, `packerName`, `orderCount`); only `packerId` optionality tightens from `string | undefined` (already matched).

### 6. `usePackingStatistics.ts`

- `getAuthenticatedApiClient().packaging_GetStatistics(fromDate, toDate)` (api-client.ts:9036) replaces `getAuthenticatedFetch()`. The hook's own `PackingStatisticsParams` stays `{ fromDate?: string; toDate?: string }` (callers pass `YYYY-MM-DD`, as `BaleniStatistics.tsx:71-72` already does via `date-fns/format`); convert to `Date` inside the `queryFn` right before calling the generated method, same pattern as `usePackagesQuery`.
- All seven hand-declared interfaces (`PackingStatisticsSummary`, `DailyThroughput`, `HourBucket`, `PackerThroughput`, `CarrierMix`, `PackagesPerOrderBucket`, `PackingStatisticsResponse`) deleted; re-export the generated equivalents under the **same local names** so `BaleniStatistics.tsx` and `PackingCharts.tsx` don't need import-path changes, only the `Date` fallout below:
  | Deleted local name | Generated replacement | Notes |
  |---|---|---|
  | `PackingStatisticsSummary` | `PackingStatisticsSummaryDto` | identical fields |
  | `DailyThroughput` | `DailyThroughputDto` | `date` is now `Date`, was `string` |
  | `HourBucket` | `HourBucketDto` | identical (no date fields) |
  | `PackerThroughput` | `PackerThroughputDto` | identical |
  | `CarrierMix` | `CarrierMixDto` | identical |
  | `PackagesPerOrderBucket` | `PackagesPerOrderBucketDto` | identical |
  | `PackingStatisticsResponse` | `GetPackingStatisticsResponse` | `fromDate`, `toDate`, `packerAttributionSince` now `Date` |
- **Consumer impact — confirmed by direct read, not assumption:**
  - `BaleniStatistics.tsx:56` — `formatDay = (iso: string) => format(parseISO(iso), ...)` must become `formatDay = (d: Date) => format(d, ...)` (drop `parseISO`, `date-fns`'s `format` accepts a `Date` directly). All three call sites (`:60,61,116`, plus `busiestDay.date` at `:165`) pass what will now be `Date` values, so they compile once `formatDay`'s parameter type changes and `parseISO(summary.busiestDay.date)` at `:165` drops its `parseISO(...)` wrapper.
  - `BaleniStatistics.tsx:59-61` (`packerAttributionHint`) — `parseISO(data.packerAttributionSince) <= parseISO(data.fromDate)` becomes a direct `Date` comparison: `data.packerAttributionSince <= data.fromDate` (native `Date` supports `<=`/`>=` via implicit numeric coercion — same as today's `parseISO` results being compared).
  - `PackingCharts.tsx:87` — `ThroughputChart`'s `format(parseISO(d.date), "dd.MM.", ...)` becomes `format(d.date, "dd.MM.", ...)`.
  - No other `date-fns` usage in this module touches statistics fields.

### 7. `useOrderTrackingNumber.ts` / `useOrderTrackingNumbers.ts`

- `getAuthenticatedApiClient().packaging_GetOrderTrackingNumber(orderCode)` / `packaging_GetOrderTrackingNumbers(orderCode)` (api-client.ts:8928,8965) replace the manual fetch.
- **Behavioral nuance to preserve:** today's code checks `response.ok` and returns `null`/`[]` on any non-2xx *without* throwing (it never reaches the `json()` parse on error). The generated client's `processPackaging_GetOrderTrackingNumber*` throws an `ApiException` via `throwException(...)` for any status other than 200/204 (api-client.ts:8957-8960). Since both hooks already wrap their fetch call in `try { ... } catch { return null / [] }`, the generated method's thrown exception is caught by the same `catch` block and produces the identical fallback — **no behavior change**, but this is a deliberate design point: do not add new `.ok`-style status handling, the existing `try/catch` already covers it.
- `data.success` check: generated `GetOrderTrackingNumberResponse`/`GetOrderTrackingNumbersResponse` both extend `BaseResponse`, so `.success` is still available and read the same way.
- No hand-declared types existed in these two files beyond the local `ApiClientWithInternals` interface (deleted).

## Data schemas

No new DTOs, no backend changes — this section documents which generated types now flow into the FE and their exact shapes, since they are the new single source of truth these hooks (and their consumers) compile against.

### Response envelope (unchanged pattern, now typed)

```ts
abstract class BaseResponse {
  success?: boolean;
  errorCode?: ErrorCodes | undefined;   // string enum — see api-client.ts:13595
  params?: { [key: string]: string } | undefined;
}
```

Every `packaging_*` response class extends this. Hooks keep the `if (!data.success) throw new Error(MAP[data.errorCode] ?? GENERIC)` pattern verbatim, only reading `data` as the typed generated class instead of `any`.

### Endpoint → method → request/response type map

| Route | Generated method | Request shape | Response type |
|---|---|---|---|
| `POST /api/packaging/orders/{orderCode}/scan?numberOfPackages=` | `packaging_ScanOrder(orderCode, numberOfPackages, body)` | `body: ScanOrderBody { packingUserId?: string }` | `ScanPackingOrderResponse { order?: ScanOrderData; shipment?: ScanShipmentData } extends BaseResponse` |
| `POST /api/packaging/orders/{orderCode}/shipment/reset?numberOfPackages=` | `packaging_ResetShipment(orderCode, numberOfPackages)` | — | `ResetOrderShipmentResponse { shipment?: ResetShipmentData } extends BaseResponse` |
| `POST /api/packaging/orders/{orderCode}/packing/complete` | `packaging_CompletePacking(orderCode)` | — | `CompletePackingOrderResponse { completed?: boolean } extends BaseResponse` |
| `GET /api/packaging/packages?...` | `packaging_GetPackages(orderCode, customerName, packageNumber, carrier: Carriers\|null, fromDate: Date\|null, toDate: Date\|null, pageNumber, pageSize, sortBy, sortDescending)` | positional | `GetPackagesResponse { items?: PackageDto[]; totalCount?; pageNumber?; pageSize? } extends BaseResponse` |
| `DELETE /api/packaging/packages/{id}` | `packaging_DeletePackage(id)` | — | `DeletePackageResponse { deleted?: boolean } extends BaseResponse` |
| `GET /api/packaging/dashboard` | `packaging_GetDashboard()` | — | `GetPackingDashboardResponse { ordersBeingPackedCount?; ordersBeingProcessedCount?; ordersBeingPackedCountLastSync?: Date; totalOrdersPackedToday; packedByPacker: PackerStatsDto[] } extends BaseResponse` |
| `GET /api/packaging/statistics?FromDate=&ToDate=` | `packaging_GetStatistics(fromDate: Date\|null, toDate: Date\|null)` | positional | `GetPackingStatisticsResponse { fromDate: Date; toDate: Date; packerAttributionSince?: Date; summary: PackingStatisticsSummaryDto; throughputDaily: DailyThroughputDto[]; hourHeatmap: HourBucketDto[]; byPacker: PackerThroughputDto[]; byCarrier: CarrierMixDto[]; packagesPerOrder: PackagesPerOrderBucketDto[] } extends BaseResponse` |
| `GET /api/packaging/orders/{orderCode}/tracking-number` | `packaging_GetOrderTrackingNumber(orderCode)` | — | `GetOrderTrackingNumberResponse { trackingNumber?: string } extends BaseResponse` |
| `GET /api/packaging/orders/{orderCode}/tracking-numbers` | `packaging_GetOrderTrackingNumbers(orderCode)` | — | `GetOrderTrackingNumbersResponse { trackingNumbers?: string[] } extends BaseResponse` |

Nested DTOs (all field-identical to today's hand-written shapes, differences called out):

- `ScanOrderData { code, customerName, shippingMethodName, cooling: Cooling, isCooled, customerNote?, eshopNote?, shippingAddress?: ShippingAddress, eligibility: ScanOrderEligibility, items: ScanPackingOrderItemDto[] }`
- `ShippingAddress { street?, city?, zip? }`, `ScanOrderEligibility { isEligible? }`, `ScanPackingOrderItemDto { name, quantity, imageUrl?, setName? }`
- `ScanShipmentData { shipmentGuid, packages: ScanShipmentPackage[], alreadyExisted, pendingCompletion? }`, `ScanShipmentPackage { trackingNumber?, labelUrl?, labelZpl? }`
- `ResetShipmentData { shipmentGuid, packages: ResetShipmentPackage[], pendingCompletion? }` — structurally identical to `ScanShipmentData` but a distinct generated class (no `alreadyExisted` field; the reset endpoint never had one).
- `PackageDto { id, orderCode, customerName, packageNumber, trackingNumber?, shippingProviderCode, shippingProviderName?, packedAt: Date, packedBy?, packedByUserId? }` — `packedAt` was `string`, now `Date`.
- `PackerStatsDto { packerId?, packerName, orderCount }`
- `DailyThroughputDto { date: Date, orderCount, packageCount }` — `date` was `string`, now `Date`.
- `HourBucketDto { dayOfWeek, hour, packageCount }`, `PackerThroughputDto { packerId?, packerName, orderCount, packageCount }`, `CarrierMixDto { code, name, packageCount }`, `PackagesPerOrderBucketDto { packageCount, orderCount }` — unchanged.
- `PackingStatisticsSummaryDto { totalPackages, totalOrders, distinctPackers, averagePackagesPerOrder, trackingCoveragePercent, busiestDay?: DailyThroughputDto, busiestHour?: HourBucketDto }`

### Enums now sourced from the generated client instead of hand-written unions

- `Cooling`: generated enum values `'None' | 'L1' | 'L2'` — identical to the hand-written union it replaces (api-client.ts:17218).
- `Carriers`: `'Zasilkovna' | 'PPL' | 'GLS' | 'Osobak'` (api-client.ts:17162) — already the type `ZasilkyFilters.tsx` builds its filter UI against; only `GetPackagesRequest.carrier`'s declared type changes from `string` to `Carriers`.
- `ErrorCodes`: all nine Czech-message-map keys used across the three mutation hooks (`ShoptetOrderNotFound`, `ShipmentCarrierNotResolved`, `ShipmentCreationFailed`, `ShipmentOrderWeightUnavailable`, `PackingUserNotEligible`, `NoShipmentToReset`, `InvalidPackageCount`, `ShipmentCancelFailed`, `PackingCompletionFailed`) confirmed present as enum members — no message-map key is dropped or needs renaming.

## Resolved design decisions (supersede the plan's open questions)

1. **Carrier enum mapping:** confirmed safe — `ZasilkyFilters.tsx` already sources its filter options from the `Carriers` enum via `CARRIER_LABELS`. Cast happens once, at the `usePackagesQuery` request boundary; no UI component changes.
2. **`errorCode` typing:** no cast needed. `ErrorCodes` (string enum) widens to `string` on read, so indexing a `Partial<Record<string, string>>` message map with a typed `errorCode` type-checks directly. All in-use error codes exist as enum members.
3. **`Date` vs `string` fields:** hooks return the generated `Date` fields unmodified (no `.toISOString()` coercion at the hook boundary). Every consumer that formats a date switches from `date-fns`'s `parseISO(string)` to passing the `Date` directly into `format(...)`, or (for `BaleniHome.tsx`/`ZasilkyTable.tsx`, which use `new Date(x)`) needs no code change since `new Date(Date)` is valid. Request-side date *inputs* (`usePackagesQuery`'s `fromDate`/`toDate` filter, `usePackingStatistics`'s params) stay `string` at the hook's public parameter boundary and convert to `Date` only immediately before the generated call — this avoids touching the filter/date-range UI components at all.
