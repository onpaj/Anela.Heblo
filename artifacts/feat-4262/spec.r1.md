# Specification: Surface Shoptet shipment-validation-failed as an actionable error on Packaging/ScanOrder instead of a generic 503

## Summary

`POST /api/packaging/orders/{orderCode}/scan` currently returns an opaque `503 Service Unavailable` for *any* failure while creating a Shoptet shipment — including a permanent `422 shipment-validation-failed` (e.g. recipient address missing city/zip), which can never succeed on retry. The packing operator sees a generic "Shoptet nemohl vytvořit zásilku — zkuste znovu." message and has no way to know the order is stuck until someone fixes the address in Shoptet. This spec adds a distinct, actionable error path for Shoptet's permanent validation failures so the operator is told what's wrong (which fields are missing) instead of being told to retry a call that cannot succeed.

## Background

On 2026-09-21, order `126020133` was scanned 5 times over 31 minutes, each attempt hitting `POST /api/shipments` on Shoptet and getting back:

```
422 {"data":null,"errors":[{"errorCode":"shipment-validation-failed","message":"Invalid recipient of order, missing fields: city, zip.","instance":"data.orderCode"}]}
```

Tracing the current code path (`ShoptetShipmentClient.CreateShipmentAsync` → `ShipmentCreationService.CreateAndPersistAsync` → `ScanPackingOrderHandler` → `PackagingController.ScanOrder`):

1. `ShoptetShipmentClient.CreateShipmentAsync` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs:172-179`) checks `response.IsSuccessStatusCode`. On any non-2xx response (422 included) it reads the raw response body as a string and throws a generic `HttpRequestException` with that string interpolated into the message — it does **not** deserialize the structured `errors[].errorCode` / `errors[].message` the way the success-path branches of this same file do for other endpoints (e.g. `FetchShipmentsAsync`, `GetShippingOptionsAsync`).
2. `ShipmentCreationService.CreateAndPersistAsync` (`backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs:80-89`) wraps the call in a blanket `catch (Exception ex)` and maps *every* failure — transient network error, Shoptet 5xx, or a permanent 422 — to the single `ErrorCodes.ShipmentCreationFailed` (2907).
3. `ErrorCodes.ShipmentCreationFailed` is annotated `[HttpStatusCode(HttpStatusCode.ServiceUnavailable)]` (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:383-384`), so `BaseApiController.HandleResponse` always returns HTTP 503 for it.
4. The frontend (`frontend/src/api/hooks/useScanPackingOrder.ts:101-107`) maps `ShipmentCreationFailed` to the static Czech message `"Shoptet nemohl vytvořit zásilku — zkuste znovu."` — telling the operator to retry a call that, for a validation failure, will never succeed.

This is a genuine 4xx client error (the order's Shoptet recipient data is invalid), not a transient fault, and nothing distinguishes the two today. The `shipment-validation-failed` error code and its known causes (missing recipient fields, missing bank account for COD, COD over carrier max) are already documented in `docs/integrations/shoptet-api.md:1197-1209`, so no new Shoptet API exploration is required — this is purely an internal error-handling/mapping fix.

## Functional Requirements

### FR-1: Distinguish permanent Shoptet validation failures from transient shipment-creation failures

When `POST /api/shipments` (Shoptet) returns a non-success status whose body is a structured Shoptet error envelope containing `errors[].errorCode == "shipment-validation-failed"`, `ShoptetShipmentClient.CreateShipmentAsync` must surface this as a distinct, typed failure carrying the Shoptet `message` and the offending `instance`/field information, rather than throwing a generic `HttpRequestException` built from the raw response body.

Any other non-success response (network failure, timeout, 5xx, an unparseable body, or a 422 with a different `errorCode`) continues to be treated as a transient/opaque failure exactly as today (generic `HttpRequestException`).

**Acceptance criteria:**
- A 422 response with `errorCode: "shipment-validation-failed"` from `POST /api/shipments` produces a distinguishable failure (not a plain `HttpRequestException`) that carries the Shoptet `message` text.
- A 422 response with any other `errorCode`, or any non-JSON/unparseable error body, falls back to today's generic `HttpRequestException` behavior — no regression for unrecognized error shapes.
- A non-2xx response with no `errors` array, or a network-level exception (e.g. `HttpRequestException` from a connection failure), also falls back to today's generic behavior.

### FR-2: Map the validation failure to a distinct, non-retryable error code and HTTP status

`ShipmentCreationService.CreateAndPersistAsync` must catch the typed validation failure from FR-1 separately from other exceptions and return a **new** error code (not `ErrorCodes.ShipmentCreationFailed`) carrying the Shoptet message as a response `Param`. This new error code must map to a 4xx status (not 503), signaling to any caller/monitoring that retrying without a data fix will not help.

All other exceptions from `CreateShipmentAsync` continue to map to `ErrorCodes.ShipmentCreationFailed` (503) exactly as today — this is strictly additive, not a replacement of the existing fallback path.

**Acceptance criteria:**
- The new error code has an `[HttpStatusCode(...)]` attribute using a 4xx status (`UnprocessableEntity`, consistent with the existing `ShipmentCarrierNotResolved` / `ShipmentLabelsNotGenerated` / `ShipmentOrderWeightUnavailable` entries in the same `29XX` range of `ErrorCodes.cs`).
- The response's `Params` dictionary contains the Shoptet validation message (and ideally the missing-fields list) so the operator sees specifics, not just a category.
- `ScanPackingOrderHandler` requires no changes — it already forwards `result.ErrorCode` and (via `BaseResponse`) `Params` unchanged from `ShipmentCreationResult` to `ScanPackingOrderResponse`.
- Existing behavior for `ErrorCodes.ShipmentCreationFailed` (503, generic message) is unchanged for every other failure mode.

### FR-3: Show the operator an actionable message instead of "try again"

The frontend's `useScanPackingOrder` hook must map the new error code to a Czech message that tells the operator the recipient address is invalid and needs to be fixed in Shoptet (not retried), and should include the specific missing-field detail from the response when available.

**Acceptance criteria:**
- `SCAN_ERROR_MESSAGES` (or its resolution function) has an entry for the new error code with wording along the lines of "Adresu příjemce nelze použít pro vytvoření zásilky (chybí povinné údaje) — opravte ji v Shoptetu." — explicitly not suggesting a retry.
- When the response `Params` carries the Shoptet message/missing-fields, the displayed message incorporates it (the `toMessage` callback passed to `callApi` already receives `params`; today's `useScanPackingOrder.ts:130` callback for this endpoint discards them — extending it to use `params` is in scope, not a rewrite of `callApi`/`apiErrorEnvelope.ts`).
- No new UI screens or components — this is a message-text/data-plumbing change within the existing scan-error toast/inline-error surface used by the packing screen.

## Non-Functional Requirements

### NFR-1: No behavior change for non-validation failures

Every existing test and behavior for `ShoptetShipmentClient`, `ShipmentCreationService`, and `ScanPackingOrderHandler` around network errors, other 4xx/5xx Shoptet responses, and the existing `ErrorCodes.ShipmentCreationFailed` 503 path must continue to pass unmodified. This is a targeted addition of one new, narrower failure branch — not a rewrite of the error-handling flow.

### NFR-2: Observability parity

The existing `_logger.LogError(ex, "Failed to create shipment for order {OrderCode}", order.Code)` call in `ShipmentCreationService` (or an equivalent) must still fire for the new validation-failure branch (at an appropriate level — this is an expected, actionable condition, not necessarily an "error"-level anomaly once this fix ships), so the condition remains visible in Application Insights and doesn't silently disappear from telemetry now that it no longer 500s/503s.

## Data Model

No persistent data model changes. This is confined to:
- A new typed exception (in-memory, adapter layer) carrying `OrderCode`, Shoptet `ErrorCode` (`"shipment-validation-failed"`), `Message`, and `Instance`/missing-fields detail parsed from `ShoptetErrorDto` (already defined in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/Dto/ShoptetErrorDto.cs`).
- A new `ErrorCodes` enum member (`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`) — no schema/migration impact, `ErrorCodes` is an in-memory enum, not persisted.
- The existing `BaseResponse.Params` dictionary is reused to carry the message to the client — no new response DTO shape.

## API / Interface Design

No new endpoints. `POST /api/packaging/orders/{orderCode}/scan` keeps its existing request/response contract (`ScanPackingOrderResponse` : `BaseResponse`); only the `ErrorCode` value and `Params` content differ for this one new failure case, and the resulting HTTP status changes from 503 to 422 **only** for this specific Shoptet validation-failure case. Existing consumers that already branch on `errorCode` (rather than assuming 503 always means "retry") are unaffected; consumers that only inspected the HTTP status will now see 422 instead of 503 for this one case, which is the intended, correct signal (permanent client error vs. transient server-side unavailability).

## Dependencies

- No new external dependencies or services.
- Depends on existing `ShoptetErrorDto` (`errorCode`, `message`, `instance` fields) already used elsewhere in `ShoptetShipmentClient` for successful-response error envelopes — the fix reuses this same DTO to parse the *non-2xx* response body, which today is only captured as a raw string.
- Depends on the existing `ErrorCodes` / `HttpStatusCodeAttribute` / `BaseApiController.HandleResponse` mapping mechanism already used by ~10 other Packaging/ShipmentLabels error codes — no new plumbing needed.

## Out of Scope

- Fixing order `126020133`'s actual recipient address in Shoptet (missing city/zip). That is a one-off data fix in Shoptet's admin UI, tracked separately from this code change, and is not something this repository's code can perform.
- Applying the same non-2xx structured-error parsing to the other `ShoptetShipmentClient` methods (`FetchShipmentsAsync`, `GetShippingOptionsAsync`, `CancelShipmentAsync`) beyond `CreateShipmentAsync`. Those already have their own generic-failure behavior and are not implicated by this incident; broadening them is a reasonable follow-up but not required to fix the reported issue.
- Any operator-facing retry/backoff logic changes on the frontend (e.g. disabling the scan button after a permanent failure). The fix is about the *message*, not the retry UX flow.
- General overhaul of `ShipmentCreationService`'s error handling beyond adding the one new branch.

## Open Questions

None.

## Status: COMPLETE
