# Implementation: forward-validation-params-through-scan-response (r1)

## Summary

Forwards the `Params` dictionary (e.g. the Shoptet validation message) from
`ShipmentCreationResult` through to `ScanPackingOrderResponse`, so a
`ShipmentValidationFailed` error surfaces its actionable detail (e.g.
"Invalid recipient of order, missing fields: city, zip.") to the packing
operator instead of being silently dropped.

## Changes

- `ScanPackingOrderResponse.cs`: added an optional `Dictionary<string, string>?
  parameters = null` parameter to the error constructor, forwarded to the
  base `ErrorCodes`/`Params` constructor. Existing call sites passing only an
  `ErrorCodes` value keep compiling unchanged.
- `ScanPackingOrderHandler.cs` (~line 118): `return new
  ScanPackingOrderResponse(result.ErrorCode!.Value, result.Params);` —
  previously `result.Params` was dropped entirely.
- `ScanPackingOrderHandlerTests.cs`:
  - Added `Handle_WhenShipmentCreationServiceFailsWithParams_ForwardsParamsUnchanged`,
    asserting `response.Params` matches the `ShipmentCreationResult.Params`
    dictionary set by the mock.
  - Added `[InlineData(ErrorCodes.ShipmentValidationFailed)]` to the existing
    `Handle_WhenShipmentCreationServiceFails_ReturnsMappedErrorCode` theory.

## Verification

- `dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ScanPackingOrderHandlerTests"` — 19/19 passed.
- `dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Packaging"` — 132/132 passed (no regression in `ScanPackingOrderHandlerPackagePersistenceTests` / `ResetOrderShipment*`, which this task leaves untouched).
- `dotnet build` (from repo root, `Anela.Heblo.sln`) — 0 errors (91 pre-existing warnings, none in touched files).
- `dotnet format --verify-no-changes` — only reports pre-existing whitespace violations in `MarketingPerformance` test files (introduced by already-merged PR #4235, untouched by this task); no violations in any file this task modified.
