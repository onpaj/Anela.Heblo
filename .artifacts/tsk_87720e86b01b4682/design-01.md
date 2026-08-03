# Design — Expedition: replace `(apiClient as any).http.fetch` with typed generated client calls

No UI section: this is a data-fetching-layer refactor only. No component is added, removed, or
re-laid-out; the four consumers (`PrintOrderModal.tsx`, `CoolingTab.tsx`, `CarrierCoolingMatrix.tsx`,
`ExpeditionListArchivePage.tsx`) keep their current props, markup, and behavior unchanged. Their
compatibility is verified below (Interfaces section) rather than redesigned.

## Component design

### `frontend/src/api/hooks/useExpeditionList.ts`

Responsibility: expose two mutation hooks over the typed generated client, translating the generated
response shape into the small result shape each consumer already expects. No transport code (fetch,
URL building, header construction) remains in this file — that's entirely the generated client's job now.

- `useRunExpeditionListPrintFix()`
  - Before: builds `${baseUrl}/api/expedition-list/run-fix`, calls `(apiClient as any).http.fetch`,
    manually branches on `response.ok`, manually parses JSON.
  - After: `mutationFn` becomes `client.expeditionList_RunFix()` (typed, no args). The method's
    `Promise<RunExpeditionListPrintFixResponse>` already throws (`SwaggerException` via the generated
    `processExpeditionList_RunFix`) on any non-2xx status, so the `!response.ok` branch is deleted, not
    ported — there is nothing left to check, the promise rejection *is* the error path.
  - Return shape narrows the generated response to the existing public contract:
    `{ totalCount: response.totalCount ?? 0 }` (`RunExpeditionListPrintFixResponse.skippedCount` exists
    but no consumer reads it, so it is not surfaced — consistent with "touch only what the task requires").

- `usePrintExpeditionOrder()`
  - Before: builds the URL/body by hand, reads the JSON body unconditionally to recover `success`/
    `errorCode`/`params` even on error status (the comment claiming a 4xx mapping), falls back to a thrown
    `Error` only if the body didn't parse as a `BaseResponse`.
  - After: `mutationFn` becomes `client.expeditionList_PrintOrder(new PrintExpeditionOrderRequest({ orderCode }))`.
    Per the plan's verified controller behavior, `PrintOrder` always returns `Ok(...)` — the generated
    client's non-2xx branch is unreachable for this route in practice, so no error handling beyond letting
    a genuine network/deserialization failure propagate as a rejected promise is added.
  - Return shape: `{ success: response.success ?? true, errorCode: response.errorCode ?? undefined, params: response.params ?? undefined }`
    — mirrors `useReprintExpeditionList` in `useExpeditionListArchive.ts:90-94` exactly (same defaulting
    rationale: the generated `BaseResponse` fields are all optional even though the backend always sends them).
  - The stale "mapped to 4xx" comment is deleted, not reworded — it described the old hand-written branch,
    which no longer exists.

### `frontend/src/api/hooks/useCarrierCooling.ts`

Responsibility: same as today (`useCarrierCoolingMatrix` query + `useSetCarrierCooling` optimistic
mutation), but `getMatrix`/`setCooling` call the generated client instead of hand-rolled fetch, and the
local DTOs stop being an independent hand-maintained copy of the wire format — they become explicit,
compiler-checked *narrowings* of the generated types, mapped in one place.

- `getMatrix()`
  - Before: manual fetch + `!response.ok` throw + `response.json()` cast straight to
    `GetCarrierCoolingMatrixResponse`.
  - After: `client.carrierCooling_GetMatrix()` returns the generated `GetCarrierCoolingMatrixResponse`
    (all fields optional, per NSwag's default). `getMatrix()` maps it into the existing local, non-optional
    shape (`groups: CarrierGroupDto[]`, `rows: CarrierCoolingRowDto[]`) the same way
    `useExpeditionListArchive.ts` already defaults `?? []` / `?? ''` for its own responses — see Data
    Schemas below for the exact mapping. This mapping is the one place that will now fail to compile if
    NSwag renames/removes a field, closing the exact silent-drift gap the task calls out.
  - `onMutate`/`onError`/`onSettled` optimistic-update logic (lines 74-115) is untouched; it only reads/writes
    `QueryClient` cache using the (unchanged) local `GetCarrierCoolingMatrixResponse` shape.

- `setCooling(request)`
  - Before: manual fetch with hand-serialized body, `!response.ok` throws a generic `Error`.
  - After: constructs the generated `SetCarrierCoolingRequest` class (imported under an alias to avoid
    colliding with the local interface of the same name — see Data Schemas) and calls
    `client.carrierCooling_SetCooling(generatedRequest)`. A non-2xx response now surfaces as the generated
    client's `SwaggerException` instead of a hand-thrown `Error`. `CoolingTab.tsx` never reads the rejected
    error's message or type (confirmed in plan investigation finding #2), so `onError`'s rollback still
    fires unchanged regardless of the rejection's shape.

- **String-literal unions `Carriers`/`DeliveryHandling`/`Cooling` stay local, not reused from the generated
  client.** Investigation for this design step found NSwag emits these as TypeScript `enum`s
  (`api-client.ts:17162`, `:17213`, `:17218`), which are nominally typed — a plain string literal like
  `'Zasilkovna'` is **not** assignable to `Carriers` when `Carriers` is that generated enum, even though the
  runtime string value matches. `CarrierCoolingMatrix.tsx` (`COOLING_OPTIONS`, `HANDLING_LABELS`) and its
  test (`CarrierCoolingMatrix.test.tsx:7`, `carrier: 'Zasilkovna'`) both use plain string literals
  throughout. Switching the exported type to the generated enum would force edits to that
  presentational component and its test — out of scope per the plan ("do not touch... unrelated"). The
  local string-literal unions are kept exactly as they are today; this is not the kind of hand-duplicated
  *response/request shape* the arch-review rule targets (those are what silently drift on a field
  rename/removal) — it's a fixed, low-cardinality mirror of backend enum *names*, which is what the file's
  existing comment already documents ("values must match the backend string serialization"). This resolves
  the plan's open question in favor of "keep the local unions."

- **`CarrierCoolingRowDto`, `CarrierGroupDto`, `GetCarrierCoolingMatrixResponse`, `SetCarrierCoolingRequest`
  interfaces stay local too**, for the same reason `useExpeditionListArchive.ts` keeps its own
  `ExpeditionListItemDto`/`GetExpeditionDatesResponse`/etc. instead of exporting the generated response
  classes directly: the generated fields are optional (NSwag default), while `CarrierCoolingMatrix.tsx`
  and its test consume them as required (`group.rows.map(...)`, `row.deliveryHandling` used as a record
  key). Re-exporting the generated optional-field types verbatim would push `undefined`-handling into the
  presentational component — out of scope. Keeping local required-field interfaces, populated by one
  explicit mapping function, is the same pattern already validated as "correct" by the sibling file. This
  supersedes the plan's FR-5, which assumed straight deletion-and-reuse was possible before this shape
  mismatch was found during design.

## Data schemas

### `expeditionList_RunFix`

Generated (`api-client.ts:3483`, response class at `:20478`):
```ts
expeditionList_RunFix(): Promise<RunExpeditionListPrintFixResponse>
// class RunExpeditionListPrintFixResponse extends BaseResponse {
//   totalCount?: number;
//   skippedCount?: number;
// }
```
Hook's public result (unchanged from today, `useExpeditionList.ts:5-7`):
```ts
export interface RunExpeditionListPrintFixResult {
  totalCount: number;
}
```
Mapping: `{ totalCount: response.totalCount ?? 0 }`.

### `expeditionList_PrintOrder`

Generated (`api-client.ts:3517`, request/response classes at `:20515`, `:20542`):
```ts
expeditionList_PrintOrder(request: PrintExpeditionOrderRequest): Promise<PrintExpeditionOrderResponse>
// class PrintExpeditionOrderRequest { orderCode?: string; }
// class PrintExpeditionOrderResponse extends BaseResponse {}  // success/errorCode/params only, via BaseResponse
```
Hook's public result: reuse `BaseResponse` from `frontend/src/types/errors.ts` (already the type
`usePrintExpeditionOrder`'s consumer, `PrintOrderModal.tsx:42-51`, is written against — `success: boolean`,
`errorCode?: ErrorCodes`, `params?: Record<string,string>`). No new type needed; `types/errors.ts`
already re-exports the same generated `ErrorCodes` enum, so `response.errorCode` (type
`ErrorCodes | undefined`) is directly assignable.

Mapping:
```ts
{
  success: response.success ?? true,
  errorCode: response.errorCode ?? undefined,
  params: response.params ?? undefined,
}
```
Request construction: `new PrintExpeditionOrderRequest({ orderCode })`.

### `carrierCooling_GetMatrix`

Generated (`api-client.ts:1728`, DTOs at `:17073`–`:17222`):
```ts
carrierCooling_GetMatrix(): Promise<GetCarrierCoolingMatrixResponse>
// class GetCarrierCoolingMatrixResponse extends BaseResponse { groups?: CarrierGroupDto[]; }
// class CarrierGroupDto { carrier?: Carriers /* enum */; rows?: CarrierCoolingRowDto[]; }
// class CarrierCoolingRowDto { deliveryHandling?: DeliveryHandling /* enum */; cooling?: Cooling /* enum */; coolingText?: string; }
```
Hook's local, required-field shape (unchanged names/shape from today, `useCarrierCooling.ts:9-22`):
```ts
export type Carriers = 'Zasilkovna' | 'PPL' | 'GLS' | 'Osobak';
export type DeliveryHandling = 'NaRuky' | 'Box';
export type Cooling = 'None' | 'L1' | 'L2';

export interface CarrierCoolingRowDto {
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
  coolingText?: string | null;
}
export interface CarrierGroupDto {
  carrier: Carriers;
  rows: CarrierCoolingRowDto[];
}
export interface GetCarrierCoolingMatrixResponse {
  groups: CarrierGroupDto[];
}
```
Mapping (the one place a NSwag field rename/removal will now fail the build instead of failing silently
at runtime):
```ts
{
  groups: (response.groups ?? []).map((g) => ({
    carrier: g.carrier as unknown as Carriers,
    rows: (g.rows ?? []).map((r) => ({
      deliveryHandling: r.deliveryHandling as unknown as DeliveryHandling,
      cooling: r.cooling as unknown as Cooling,
      coolingText: r.coolingText ?? null,
    })),
  })),
}
```
The `as unknown as X` casts are required because TS string enums are nominal types, not structurally
assignable to a plain string-literal union even when every member's runtime value matches (see Component
design above) — this is a value-preserving cast, not a type-safety hole, since the generated enum's
member set is exactly `Carriers`/`DeliveryHandling`/`Cooling`'s literal set.

### `carrierCooling_SetCooling`

Generated (`api-client.ts:1762`, DTOs at `:17224`–`:17297`):
```ts
carrierCooling_SetCooling(request: SetCarrierCoolingRequest): Promise<SetCarrierCoolingResponse>
// class SetCarrierCoolingRequest { carrier?: Carriers; deliveryHandling?: DeliveryHandling; cooling?: Cooling; coolingText?: string; }
// class SetCarrierCoolingResponse extends BaseResponse {}  // no extra fields
```
Hook's local request shape stays as today (required fields, `useCarrierCooling.ts:24-29`) — this is the
type `CarrierCoolingMatrix.tsx`'s `onSetCooling` callback is written against and must not change:
```ts
export interface SetCarrierCoolingRequest {
  carrier: Carriers;
  deliveryHandling: DeliveryHandling;
  cooling: Cooling;
  coolingText?: string | null;
}
```
To avoid a name collision with the generated class of the same name, the generated class is imported
under an alias, e.g. `import { SetCarrierCoolingRequest as GeneratedSetCarrierCoolingRequest } from '../generated/api-client'`.
`setCooling(request)` (the local, required-field type) builds the wire request as:
```ts
new GeneratedSetCarrierCoolingRequest({
  carrier: request.carrier as unknown as GeneratedCarriers,
  deliveryHandling: request.deliveryHandling as unknown as GeneratedDeliveryHandling,
  cooling: request.cooling as unknown as GeneratedCooling,
  coolingText: request.coolingText ?? undefined,
})
```
`mutationFn: setCooling` keeps returning `Promise<void>` (its resolved `SetCarrierCoolingResponse` carries
no fields beyond `BaseResponse`'s success/errorCode/params, none of which `CoolingTab.tsx` reads on the
success path today — only the reject path matters, per finding #2).

### Test doubles (mocking surface change)

Both hook test files stop mocking `{ baseUrl, http: { fetch } }` on `getAuthenticatedApiClient()` and
instead mock the four typed methods directly, matching the precedent in
`useExpeditionListArchive.test.ts:44-49`:
```ts
mockGetAuthenticatedApiClient.mockReturnValue({
  expeditionList_RunFix: mockRunFix,
  expeditionList_PrintOrder: mockPrintOrder,
} as any);
```
and, for the new `useCarrierCooling.test.ts`:
```ts
mockGetAuthenticatedApiClient.mockReturnValue({
  carrierCooling_GetMatrix: mockGetMatrix,
  carrierCooling_SetCooling: mockSetCooling,
} as any);
```
`mockGetMatrix`/`mockPrintOrder`/etc. resolve or reject with instances of the generated response/error
classes (or plain objects matching their shape), not raw `Response`-like fetch mocks — there is no `.ok`/
`.json()` surface left to fake once the transport is the generated client.

## Interfaces (consumer compatibility — verified, not redesigned)

| Consumer | Reads from hook | Still satisfied after change? |
|---|---|---|
| `PrintOrderModal.tsx:43-49` | `result.success`, `result.errorCode`, `result.params` (`types/errors.ts` `BaseResponse`) | Yes — `usePrintExpeditionOrder` still resolves this exact shape; `errorCode`'s type (`ErrorCodes \| undefined`) is the same re-exported generated enum already used by `getErrorMessage`. |
| `ExpeditionListArchivePage.tsx:131` | `result.totalCount` | Yes — `RunExpeditionListPrintFixResult.totalCount` unchanged. |
| `CoolingTab.tsx:9-33` | `data.groups`, `mutate`, `isPending`, `variables` (`savingRow`) | Yes — `useCarrierCoolingMatrix` still resolves `{ groups: CarrierGroupDto[] }`; `useSetCarrierCooling`'s mutation variable type (local `SetCarrierCoolingRequest`) is unchanged. |
| `CarrierCoolingMatrix.tsx` + its test | `CarrierGroupDto`, `CarrierCoolingRowDto`, `Carriers`, `DeliveryHandling`, `Cooling`, `SetCarrierCoolingRequest` imported from `useCarrierCooling.ts`, all required-field / string-literal | Yes — all five stay exported from the same module with the same shape; only their *internal* construction (mapped from the generated client instead of `response.json()`) changes. |

No consumer file needs an edit for this task.

## Non-functional / cross-cutting notes carried from the plan

- No new `as any` casts anywhere in either hook file (the `as unknown as <LocalUnion>` casts above are
  distinct: they narrow one closed, verified-matching TypeScript type to another, not "trust me, ignore the
  compiler" escapes onto private client internals).
- `useExpeditionListArchive.ts` is not touched — it is already the reference implementation this design
  follows.
- Backend/controller behavior (`RunFix`/`PrintOrder` always-200 vs. `SetCooling`'s `HandleResponse`) is not
  changed; this is a frontend transport-layer swap only, per the plan's explicit scope boundary.
