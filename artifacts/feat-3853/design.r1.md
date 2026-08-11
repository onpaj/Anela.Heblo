# Design: Extract shared shipment-creation logic (ScanPackingOrder / ResetOrderShipment)

## Component Design

### `IShipmentCreationService` / `ShipmentCreationService`

**Location:** `Anela.Heblo.Application.Features.Packaging.Services` (new folder `Packaging/Services/`), per the arch review's Decision 1 — Packaging already couples unguarded to `ShipmentLabels`, and the collaborator's defining responsibility (`Package` persistence) is Packaging-owned.

**Responsibility:** Own the entire "resolve weight → resolve carrier → create shipment → fetch, filter, and pad labels → resolve packer → persist `Package` rows" sequence, replacing the hand-duplicated logic currently split across `ScanPackingOrderHandler` and `ResetOrderShipmentHandler`. It is a plain application-service class (not a MediatR handler), independently constructible with mocked dependencies.

**Contract:**

```csharp
public interface IShipmentCreationService
{
    Task<ShipmentCreationResult> CreateAndPersistAsync(
        PackingOrder order,
        int numberOfPackages,
        Guid? packingUserId,
        CancellationToken ct);
}
```

- Caller has already fetched `order` (via `IPackingOrderClient`) — the service never fetches it itself (NFR-1: no added round-trip).
- `packingUserId` is `null` for Reset today, optionally set for Scan; the service must treat `null` and non-null uniformly (see Packer resolution below).
- Return value is always a fully-populated `ShipmentCreationResult`, never a thrown exception for expected failure branches (invalid count, carrier not resolved, shipment-creation failure) — those map to `IsSuccess = false` + `ErrorCode`. Persistence failures are the one exception that is caught and swallowed internally (see below); they do not surface as `IsSuccess = false`.

**Internal steps, in order:**

1. **Package-count validation.** Reject `numberOfPackages` outside `1..10` with `ErrorCodes.InvalidPackageCount`, short-circuiting before any external call.
2. **Weight computation.** Sum order line weights; if the total is zero, fall back to `ShipmentLabelsSettings.FallbackPackageWeightGrams` and log a warning. Per-package weight is `Math.Max(totalWeightGrams / numberOfPackages, MinPackageWeightGrams)`.
3. **Carrier resolution.** Call `IShipmentClient.GetShippingOptionsAsync`; if it returns no options, return `ErrorCodes.ShipmentCarrierNotResolved`.
4. **Shipment creation.** Build `CreateShipmentCommand` and call `IShipmentClient.CreateShipmentAsync`; catch any exception and return `ErrorCodes.ShipmentCreationFailed`.
5. **Label fetch, filter, and pad.** Call `IShipmentClient.GetLabelsByOrderCodeAsync(order.OrderCode)`, then **filter to `label.ShipmentGuid == createdShipment.ShipmentGuid`** before padding to exactly `numberOfPackages` entries (null-filled where Shoptet hasn't generated a label yet). The filter is mandatory for both callers — Scan is safe without it only by the accident of running solely on the "no existing shipment" branch; folding the two implementations together removes that guarantee, so the service must always filter (arch review Decision 4).
6. **Packer resolution.** If `packingUserId` is non-null: one `IAuthorizationRepository.GetUserByIdAsync` call, reused for both the `PackingUserNotEligible` eligibility gate and to populate `PackedByUserId`/`PackedBy` (display name) — consolidating what are today two separate lookups in `ScanPackingOrderHandler`. The eligibility gate (`ErrorCodes.PackingUserNotEligible`) is only evaluated when `packingUserId` is non-null, so Reset (always `null`) is never gated. If `packingUserId` is `null`: `PackedByUserId` stays `null`, `PackedBy` is set from `ICurrentUserService.GetCurrentUser().Email`.
7. **Persistence.** Build one `Package` row per index in the **padded** (`numberOfPackages`-length) label list — not per raw fetched-label count — with `PackageNumber = (index + 1).ToString(CultureInfo.InvariantCulture)` and `ShipmentGuid = createdShipment.ShipmentGuid`. Call `IPackageRepository.ReplacePackagesForOrderAsync(order.OrderCode, packages, ct)`. Building rows from the padded list (rather than `labels.Count`) is a correctness fix that applies to both callers uniformly (arch review Decision 5): it ensures a package whose label Shoptet hasn't generated yet still gets a row (`TrackingNumber = null`), which `FillTrackingNumbersJob` can later backfill — today's Scan path silently drops that row.
8. **Persistence-failure handling.** A thrown exception from step 7 is caught inside the service, logged as a structured warning (`OrderCode`, `ShipmentGuid`, `PackageCount`), and swallowed — it does not change `ShipmentCreationResult.IsSuccess`. This preserves today's Scan behavior and applies it symmetrically to Reset (arch review Decision 2 / spec Open Question 1, resolved: swallow-and-log for both).

**Dependencies:** `IShipmentClient`, `IPackageRepository`, `IAuthorizationRepository`, `ICurrentUserService`, `IOptions<ShipmentLabelsSettings>`, `ILogger<ShipmentCreationService>` — all pre-existing interfaces, only re-wired to a new owner.

### `ScanPackingOrderHandler` (refactored)

Retains: eligibility check and non-eligible-order early return, the existing-shipment reprint/backfill path (`BackfillExistingShipmentPackagesAsync`, still using `IPackageRepository.AddMissingAsync` directly — out of scope for this feature), deferred `TryMarkAsPackedAsync` semantics, and the `PendingCompletion = true` response flag.

Changes: the "no existing shipment, eligible order" branch now calls `_shipmentCreationService.CreateAndPersistAsync(order, request.NumberOfPackages, request.PackingUserId, ct)` instead of running its own inline weight/carrier/creation/label/persistence logic. The handler maps `ShipmentCreationResult` to `ScanShipmentData`/`ScanShipmentPackage` (`TrackingNumber`, `LabelUrl`, `LabelZpl` per label) and to its existing error-code responses. May drop its direct `IAuthorizationRepository`/`ICurrentUserService` usage for this path since it moves into the service; keeps `IPackageRepository` only for the backfill path.

### `ResetOrderShipmentHandler` (refactored)

Retains: cancellation of prior shipment(s), `NoShipmentToReset`/`ShipmentCancelFailed` handling, `PendingCompletion = numberOfPackages >= 2`, and its own order fetch (unchanged — the service does not re-fetch).

Changes: after cancelling the prior shipment(s) and fetching `order`, calls `_shipmentCreationService.CreateAndPersistAsync(order, request.NumberOfPackages, null, ct)` instead of its own inline duplicate of the create-shipment block. This is the bug fix: for the first time, Reset causes `IPackageRepository.ReplacePackagesForOrderAsync` to run, with rows carrying the **new** shipment's GUID, replacing (not appending to) whatever rows existed for the order code — which also clears the stale rows left by the cancelled shipment(s), since `ReplacePackagesForOrderAsync` is delete-then-insert per order code. Gains `IShipmentCreationService` as a new constructor dependency; loses its own duplicated weight/carrier/creation/label logic.

### DI wiring

`PackagingModule.AddPackagingModule()` registers `services.AddScoped<IShipmentCreationService, ShipmentCreationService>();` alongside the existing `IPackageRepository` registration.

### Module boundary test update

`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`'s `PackagingShoptetOrdersAllowlist` must gain an entry for `Anela.Heblo.Application.Features.Packaging.Services.ShipmentCreationService -> Anela.Heblo.Application.Features.ShoptetOrders.PackingOrder`, since the service's signature takes a `PackingOrder` parameter and the existing allowlist only names the two handlers, not the new service. Without this the boundary test fails on first build.

## Data Schemas

No `Package` entity or database schema changes. `Package` (`backend/src/Anela.Heblo.Domain/Features/Packaging/Package.cs`) is unchanged:

```
Package { Id, OrderCode, CustomerName, PackageNumber, TrackingNumber?, ShippingProviderCode, ShippingProviderName?, ShipmentGuid, PackedAt, PackedBy?, PackedByUserId?, CreatedAt }
```

`IPackageRepository.ReplacePackagesForOrderAsync(orderCode, packages, ct)` is the existing persistence entry point (delete-then-insert per order code); its semantics are unchanged and are exactly what makes Reset idempotent once the collaborator starts calling it.

### New internal type: `ShipmentCreationResult`

Class (not record, per project DTO convention — even though this type is never serialized to the client, it follows the same convention for consistency). Lives alongside the service in `Packaging/Services/`.

```csharp
public class ShipmentCreationResult
{
    public bool IsSuccess { get; init; }
    public ErrorCodes? ErrorCode { get; init; }          // set when IsSuccess == false
    public Guid ShipmentGuid { get; init; }
    public string CarrierCode { get; init; } = null!;
    public string? CarrierName { get; init; }
    public IReadOnlyList<ShipmentLabel> Labels { get; init; } = [];
    // exactly `numberOfPackages` entries: filtered to this shipment's GUID,
    // padded with null-fields entries where Shoptet hasn't generated a label yet
}
```

### Unchanged external contracts

- `ScanPackingOrderRequest` / `ScanPackingOrderResponse` / `ScanShipmentData` / `ScanShipmentPackage` — no field changes.
- `ResetOrderShipmentRequest` / `ResetOrderShipmentResponse` / `ResetShipmentData` / `ResetShipmentPackage` — no field changes; `ResetOrderShipmentRequest` still has no `PackingUserId` field (Reset always passes `packingUserId: null` into the service).
- OpenAPI-generated TypeScript client — no regeneration needed; no REST endpoint signature changes.
- `PackingShipmentCreator.tsx` — no frontend changes.

### Error codes (existing, re-owned by the service, unchanged values)

- `ErrorCodes.InvalidPackageCount` — `numberOfPackages` outside `1..10`.
- `ErrorCodes.ShipmentCarrierNotResolved` — `GetShippingOptionsAsync` returns no options.
- `ErrorCodes.ShipmentCreationFailed` — `CreateShipmentAsync` throws.
- `ErrorCodes.PackingUserNotEligible` — packer-eligibility gate, evaluated only when `packingUserId` is non-null.

### Structured log fields (persistence-failure path)

On a caught exception from `ReplacePackagesForOrderAsync`, the warning log includes `OrderCode`, `ShipmentGuid`, and `PackageCount` as structured fields (expanded from today's `OrderCode`-only logging in Scan), since this is now the single failure point both callers share.
