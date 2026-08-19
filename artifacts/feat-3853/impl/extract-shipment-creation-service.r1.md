# Implementation: extract-shipment-creation-service

## What was implemented

Extracted the shared "resolve weight → resolve carrier → create shipment → fetch/filter/pad
labels → resolve packer → persist `Package` rows" orchestration that was copy-pasted between
`ScanPackingOrderHandler` and `ResetOrderShipmentHandler` into a new collaborator,
`IShipmentCreationService` / `ShipmentCreationService`, in
`Anela.Heblo.Application.Features.Packaging.Services`. This is task 1 of 3 — the two handlers
are not yet refactored to call it (that is tasks 2 and 3).

The new service's `CreateAndPersistAsync(PackingOrder order, int numberOfPackages, Guid? packingUserId, CancellationToken ct)`:
1. Validates `numberOfPackages` against `MaxPackages = 10` (`InvalidPackageCount`).
2. Computes total order weight, falling back to `ShipmentLabelsSettings.FallbackPackageWeightGrams`
   when all items report zero weight (with a warning log), then per-package weight via
   `Math.Max(total / n, MinPackageWeightGrams)`.
3. Resolves carrier via `IShipmentClient.GetShippingOptionsAsync` (`ShipmentCarrierNotResolved` if none).
4. Creates the carrier shipment via `IShipmentClient.CreateShipmentAsync`, catching failures as
   `ShipmentCreationFailed`.
5. Fetches labels via `GetLabelsByOrderCodeAsync`, **filters to `ShipmentGuid == createdShipment.ShipmentGuid`**
   (preserving the filter that only `ResetOrderShipmentHandler`'s copy had, needed because a
   just-cancelled prior shipment's labels can still come back from the API), then pads to exactly
   `n` entries with empty-field placeholders for labels Shoptet hasn't generated yet.
6. Resolves the packer: if `packingUserId` is given, looks up and validates eligibility
   (`PackingUserNotEligible` if inactive/ineligible); otherwise falls back to the current logged-in
   user's email (Reset's existing behavior, no `PackingUserId` in its request DTO).
7. Persists `Package` rows via `IPackageRepository.ReplacePackagesForOrderAsync`, built from the
   **padded `n`-length label list** (not the raw fetched-label count — this fixes a second latent
   bug the architect found, where `ScanPackingOrderHandler`'s original code persisted from the
   unpadded list and could under-persist rows when Shoptet lagged on label generation). Persistence
   failures are swallowed and logged (`LogWarning`), matching existing Scan behavior — the carrier
   shipment already exists at that point, so a hard failure would be worse than a stale/incomplete
   `Package` row a future job/reset can retry.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/IShipmentCreationService.cs` — new interface.
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` — new implementation.
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs` — new result DTO (class, not record, per project convention). Carries `IsSuccess`, `ErrorCode`, `ShipmentGuid`, `CarrierCode`, `CarrierName`, `Labels`.
- `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs` — registered `IShipmentCreationService -> ShipmentCreationService` as scoped.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added 3 allowlist entries for the new service's necessary cross-module references to `Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder` / `PackingOrderItem` (interface signature, class implementation, and the compiler-generated lambda-closure nested type for `order.Items.Sum(...)`), following the existing `Packaging -> ShoptetOrders` allowlist pattern.
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs` — new unit test suite for the service (see below).

## Tests

`ShipmentCreationServiceTests.cs` — unit tests covering: package-count validation bounds, zero-weight fallback + warning log, per-package weight calculation, carrier-not-resolved error path, shipment-creation-failure error path, label fetch/filter-by-shipment-GUID/pad-to-n behavior (including the case where stale labels from a different `ShipmentGuid` must be excluded), packer eligibility validation (both the explicit-packer and fallback-to-current-user paths), persistence via `ReplacePackagesForOrderAsync` using the padded list, and persistence-failure swallow-and-log behavior.

`ModuleBoundariesTests.cs` — existing architecture test suite; extended with the 3 new allowlist entries so it continues to pass with the new service's necessary cross-module type references.

## How to verify

```
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ShipmentCreationServiceTests|FullyQualifiedName~ModuleBoundariesTests"
```

Build: 0 errors (pre-existing warnings only, unrelated to this change).
Tests: 50/50 passed (ShipmentCreationServiceTests + ModuleBoundariesTests combined).

## Notes

- Package-count validation intentionally still needs to remain duplicated in the two handlers
  (per the task plan) as a fast pre-check before any handler-level I/O — existing handler tests
  assert the order/shipment client is never called for invalid counts — even though the service
  also validates independently for direct callers. That handler-level duplication is out of scope
  for this task (tasks 2/3 handle it) and is a deliberate, narrow exception noted in the
  architecture review, not new duplication introduced by this task.
- No production behavior changed yet — the two handlers still run their own copy-pasted logic
  until tasks 2 and 3 wire them to this service. This task only adds the new service and its
  tests; it does not yet fix the Reset persistence bug (that lands in task 3).

## PR Summary

Adds `IShipmentCreationService` / `ShipmentCreationService` to `Features.Packaging.Services`,
consolidating the "create carrier shipment → map to n packages → persist Package rows"
orchestration that `ScanPackingOrderHandler` and `ResetOrderShipmentHandler` currently duplicate.
The new service intentionally preserves the shipment-GUID label filter (previously only present
in Reset's copy) and persists from the padded n-length label list rather than the raw fetched-label
count (fixing a second latent under-persistence bug found during architecture review). Registered
in DI via `PackagingModule.cs`; three `ModuleBoundariesTests.cs` allowlist entries added for the
service's necessary references to `ShoptetOrders.PackingOrder`/`PackingOrderItem`. Covered by a
new `ShipmentCreationServiceTests.cs` unit suite. The two handlers are not yet refactored to use
this service — that is done in follow-up tasks.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/IShipmentCreationService.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs` (new)
- `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs` (DI registration)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (allowlist entries)
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs` (new tests)

## Status
DONE
