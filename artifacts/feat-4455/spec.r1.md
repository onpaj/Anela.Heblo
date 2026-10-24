# Specification: Decouple GiftPackageManufactureService from Manufacture-owned IManufactureClient

## Summary
`GiftPackageManufactureService` (Logistics) injects `IManufactureClient`, a 9-method Manufacture-owned domain interface, to call a single method (`GetSetPartsAsync`). Replace this with a slim Logistics-owned BOM contract, implemented by an adapter inside the Manufacture module and registered by `ManufactureModule`, consistent with the consumer-defines-the-contract rule in `development_guidelines.md`.

## Background
The brief comes from the daily arch-review routine (2026-10-07). The codebase already uses the pattern: `ILeafletKnowledgeSource` (Leaflet-owned, KnowledgeBase adapter), `IInventoryReservationService` (Logistics-owned, `ManufactureInventoryReservationAdapter`), `ICatalogManufactureSource` (Catalog-owned, `ManufactureCatalogSourceAdapter`). `ModuleBoundariesTests` currently allowlists two Logistics -> Manufacture edges for this violation (`IManufactureClient` and `ProductPart`), with comments saying to remove them once decoupled.

Single call site: `GiftPackageManufactureService.GetGiftPackageDetailAsync` (line ~104) calls `GetSetPartsAsync(giftPackageCode, ct)` and uses `ProductCode`, `ProductName`, `Amount` of each part.

## Functional Requirements

### FR-1: Logistics-owned BOM contract
Add `IGiftPackageBomSource` with one method `GetSetPartsAsync(string setProductCode, CancellationToken)` returning a Logistics-owned model list (not Manufacture's `ProductPart`).
**Acceptance criteria:**
- Interface and model live under `Anela.Heblo.Application.Features.Logistics.Contracts` (next to `ILogisticsCatalogSource`) and reference no `Anela.Heblo.Domain.Features.Manufacture` types.
- Model is a class (not a record), with `ProductCode`, `ProductName`, `Amount` (double).

### FR-2: Manufacture-side adapter
Add an internal adapter in `Features/Manufacture/Infrastructure/` implementing `IGiftPackageBomSource` by delegating to `IManufactureClient.GetSetPartsAsync` and mapping `ProductPart` to the Logistics model.
**Acceptance criteria:**
- Adapter maps all three fields 1:1 and preserves order and empty-list results.
- Exceptions from the client propagate unchanged.

### FR-3: Provider-owned DI registration
Register `IGiftPackageBomSource -> adapter` (scoped) in `ManufactureModule.AddManufactureModule`, with a comment like the neighbouring cross-module registrations.
**Acceptance criteria:**
- Resolving `IGiftPackageManufactureService` from the full app container succeeds.

### FR-4: Consumer switched
`GiftPackageManufactureService` takes `IGiftPackageBomSource` instead of `IManufactureClient`; no `using Anela.Heblo.Domain.Features.Manufacture`.
**Acceptance criteria:**
- Behaviour of `GetGiftPackageDetailAsync` (ingredient mapping, stock, ordering) is unchanged.

### FR-5: Architecture allowlist cleanup
Remove the two Logistics -> Manufacture entries from `LogisticsAllowlist` in `ModuleBoundariesTests` (the allowlist becomes empty).
**Acceptance criteria:**
- `ModuleBoundariesTests` passes with the empty Logistics allowlist.

### FR-6: Tests updated
Update `GiftPackageManufactureServiceTests`, `GiftPackageManufactureAtomicityIntegrationTests` and any other test constructing the service to mock `IGiftPackageBomSource`. Add a unit test for the adapter.
**Acceptance criteria:** all touched tests pass.

## Non-Functional Requirements

### NFR-1: Performance
No change; one extra in-process delegation and a small list mapping.

### NFR-2: Security
No change; no endpoints, auth or data exposure altered.

## Data Model
No persistence changes. New transient model `LogisticsGiftPackagePart { ProductCode, ProductName, Amount }`.

## API / Interface Design
No HTTP/OpenAPI change. Internal C# contract only:
`Task<List<LogisticsGiftPackagePart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default)`.

## Dependencies
- `IManufactureClient` (Flexi adapter) stays the data source behind the adapter.
- `ModuleBoundariesTests` architecture test.
- Process docs: this is a pure refactor with no behaviour change; if CI process-docs check flags the touched code, bump `verified_at` on the relevant gift package doc.

## Out of Scope
- Catalog -> Manufacture `IManufactureClient` violations (separate allowlist).
- Changing `IManufactureClient` or `ProductPart`.
- Any behaviour or UI change.

## Open Questions
None.

## Status: COMPLETE
