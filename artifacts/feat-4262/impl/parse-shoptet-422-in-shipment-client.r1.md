# Implementation: parse-shoptet-422-in-shipment-client

## What was implemented
`ShoptetShipmentClient.CreateShipmentAsync` now distinguishes Shoptet's permanent
`shipment-validation-failed` error code (returned as an HTTP 422 with a specific
`errorCode` in the response body) from every other non-2xx response. On a match it
throws the existing `ShoptetShipmentValidationException` (added by the prior task,
`add-validation-exception-and-error-code`) carrying the order code, the carrier's
error code, message, and `instance` field. Every other non-2xx case — including a
422 with a different `errorCode`, and a body that isn't valid JSON at all — keeps
throwing the pre-existing generic `HttpRequestException`, unchanged.

A private `TryParseValidationError` helper does a best-effort JSON parse of the
response body into the existing `ShoptetCreateShipmentResponse` DTO and looks for
an error whose `ErrorCode == "shipment-validation-failed"`. A `JsonException` (body
not valid JSON, e.g. an upstream proxy's HTML error page) is caught and treated as
"no validation error found," falling through to the generic path — this can never
crash the caller.

## Files created/modified
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` —
  added the `TryParseValidationError` helper and the branch in `CreateShipmentAsync`
  that throws `ShoptetShipmentValidationException` when it matches.
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs` —
  added three tests per the task spec.

## Tests
- `CreateShipmentAsync_ShipmentValidationFailed_ThrowsShoptetShipmentValidationException` —
  the exact body from the 2026-09-21 incident (order 126020133); asserts all four
  exception properties (`OrderCode`, `ShoptetErrorCode`, `Message`, `Instance`).
- `CreateShipmentAsync_OtherValidationErrorCode_ThrowsGenericHttpRequestException` —
  a 422 with a different `errorCode` must NOT be classified as the permanent case.
- `CreateShipmentAsync_NonJsonErrorBody_FallsBackToGenericHttpRequestException` —
  a non-JSON (HTML) error body on a 503 must not crash and must fall back to the
  generic path.

All pre-existing tests in the file (including the existing 422-with-empty-body
generic-exception test) pass unchanged.

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetShipmentClientTests"
dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShoptetApi"
```
Results: `ShoptetShipmentClientTests` — 28/28 passed. Full `ShoptetApi` filter —
215/215 passed (no regressions).

## Notes
No new `using` directives were needed — `ShoptetCreateShipmentResponse`/`ShoptetErrorDto`
(`Dto` namespace) and `ShoptetShipmentValidationException`
(`Anela.Heblo.Application.Features.ShipmentLabels`) were already imported at the top
of both files, exactly as the task context predicted. No deviations from the task
spec.

## PR Summary
`ShoptetShipmentClient.CreateShipmentAsync` now parses the response body of a
failed `POST /api/shipments` call and throws the dedicated
`ShoptetShipmentValidationException` when Shoptet reports its permanent
`shipment-validation-failed` error code, instead of the generic
`HttpRequestException` used for every other failure. This lets upstream code
(a later task in this feature) distinguish a permanent validation failure — e.g.
a recipient address missing required fields — from a transient/unclassified
error that may still succeed on retry.

### Changes
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Shipments/ShoptetShipmentClient.cs` — added `TryParseValidationError` and the new exception branch in `CreateShipmentAsync`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetShipmentClientTests.cs` — three new tests covering the match, the non-match (different errorCode), and the non-JSON-body fallback

## Status
DONE
