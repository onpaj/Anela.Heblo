# Code Review: parse-shoptet-422-in-shipment-client

## Summary
The implementation follows the task-context's specified diff exactly: a new
`TryParseValidationError` helper best-effort parses a non-2xx response body for
Shoptet's `shipment-validation-failed` error code, and `CreateShipmentAsync` throws
`ShoptetShipmentValidationException` when it matches, falling back to the existing
generic `HttpRequestException` for every other case (different error code, or
unparseable body). All three required tests were added and pass, alongside the
full pre-existing suite for this file and the broader `ShoptetApi` filter.

## Review Result: PASS

### task: parse-shoptet-422-in-shipment-client
**Status:** PASS

Verified:
- Spec compliance: matches the task-context's Step 3 diff verbatim (helper
  placement, exception construction, fallback behavior).
- Architecture adherence: mirrors the existing error-parsing pattern used by
  `FetchShipmentsAsync`/`GetShippingOptionsAsync` in the same class; no new
  `using` directives needed (both required namespaces were already imported).
- Completeness: all 3 specified tests present and passing
  (`ShipmentValidationFailed_Throws...`, `OtherValidationErrorCode_Throws...`,
  `NonJsonErrorBody_FallsBackTo...`). `dotnet test --filter ShoptetShipmentClientTests`
  → 28/28 passed. `dotnet test --filter ShoptetApi` → 215/215 passed (no
  regressions to `FetchShipmentsAsync`/`GetShippingOptionsAsync`/`CancelShipmentAsync`,
  confirming NFR-1).
- Correctness: `JsonException` is caught around the deserialize call so a
  non-JSON body (e.g. an HTML error page) cannot crash the caller — confirmed by
  the `NonJsonErrorBody` test. The `validationError.ErrorCode!` null-forgiving
  operator is safe: `TryParseValidationError` only returns a non-null result when
  `FirstOrDefault` matched the literal string `"shipment-validation-failed"`, so
  `ErrorCode` is guaranteed non-null at that point.

## Docs to Update
None — `shipment-validation-failed` and its known causes are already documented
in `docs/integrations/shoptet-api.md` (added by an earlier task on this branch).

## Overall Notes
No cross-cutting concerns. Clean, minimal diff scoped exactly to the task.
