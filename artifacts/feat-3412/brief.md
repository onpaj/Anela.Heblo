## Module / File
`backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/ScanPackingOrder/ScanPackingOrderHandler.cs`

## Coverage
Line coverage: 32.9% (filter threshold: 60%)
3 existing test files: `ScanPackingOrderHandlerTests`, `ScanPackingOrderHandlerPackagePersistenceTests`, `ScanPackingOrderPackerTests`.

## What&#39;s not tested

**Zero-weight fallback**:
- When all order items have `WeightGrams == 0` (total weight = 0), the handler falls back to `_shipmentSettings.FallbackPackageWeightGrams` with a warning log, because carriers reject 0 kg packages. No test covers this branch. If the fallback is missing from the settings configuration, `perPackageWeightGrams` could end up as 0 or negative (after the `Math.Max` against `MinPackageWeightGrams`), which would cause a carrier API rejection that&#39;s hard to diagnose in production.

**Packer eligibility guard**:
- When `request.PackingUserId` is non-null, the handler fetches the user and validates `packer != null &amp;&amp; packer.IsActive &amp;&amp; packer.CanPack`. The `ScanPackingOrderPackerTests` file exists, but the specific branches — packer not found (`null`), packer found but `IsActive = false`, packer found but `CanPack = false` — should be verified to each independently return `PackingUserNotEligible`.

**`BackfillExistingShipmentPackagesAsync` path**:
- When an eligible order already has existing labels (`existingLabels.Count &gt; 0` and `isEligible = true`), the handler calls `BackfillExistingShipmentPackagesAsync` then `TryMarkAsPackedAsync`. This path (a rescan of an in-progress but not yet completed pack) is not covered.

## Why it matters
A zero-weight order that silently gets an invalid package weight will fail at the carrier API with a cryptic error rather than a clear internal fallback message. A packer eligibility bug would allow an inactive or non-packer user to be attributed to a shipment, breaking packing traceability.

## Suggested approach
- Add a test where all order items have `WeightGrams = 0` and `ShipmentLabelsSettings.FallbackPackageWeightGrams = 500`. Assert the shipment command is created with `WeightGrams = 500`.
- Add three packer-eligibility tests: null user, inactive user, user with `CanPack = false` — each should return `PackingUserNotEligible`.
- Add a test for `isEligible = true` with pre-existing labels — assert `BackfillExistingShipmentPackagesAsync` and `TryMarkAsPackedAsync` are called. ~0.5 day effort.

---
_Filed by weekly coverage-gap routine on 2026-06-29. Based on CI run #28295125598 (23c3b5d571c976074ee31869c96e29487098040c)._
