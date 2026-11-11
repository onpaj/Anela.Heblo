# Architecture Review: Decouple GiftPackageManufactureService from IManufactureClient

## Skip Design: true
Backend-only refactor; no UI.

## Architectural Fit Assessment
Fits exactly the established cross-module pattern (consumer owns contract in `Features/<Consumer>/Contracts`, provider adapter in `Features/<Provider>/Infrastructure`, provider's `*Module` owns DI). Verified precedents: `ILeafletKnowledgeSource` + `KnowledgeBaseLeafletSourceAdapter`, `IInventoryReservationService` + `ManufactureInventoryReservationAdapter` (registered in `ManufactureModule`), `ICatalogManufactureSource` + `ManufactureCatalogSourceAdapter`. The only integration point is `GetGiftPackageDetailAsync`.

## Proposed Architecture

### Component Overview
```
Logistics.GiftPackageManufactureService --> Logistics.Contracts.IGiftPackageBomSource
                                                     ^ implemented by
Manufacture.Infrastructure.ManufactureGiftPackageBomAdapter --> Domain.Manufacture.IManufactureClient (Flexi)
ManufactureModule registers IGiftPackageBomSource -> adapter
```

### Key Design Decisions

#### Decision 1: Contract location
**Options considered:** (a) `UseCases/GiftPackageManufacture/Services/` as in the brief; (b) `Logistics/Contracts/`.
**Chosen approach:** (b) `Features/Logistics/Contracts/IGiftPackageBomSource.cs`.
**Rationale:** All other cross-module consumer contracts live in the module's `Contracts/` folder, and the Manufacture adapter must be able to reference it; `Services/` is an internal implementation folder.

#### Decision 2: Own return model instead of `ProductPart`
**Options considered:** reuse Domain `ProductPart`; Logistics-owned model.
**Chosen approach:** `Logistics/Contracts/Models/LogisticsGiftPackagePart` (class).
**Rationale:** Reusing `ProductPart` would keep a Logistics -> Manufacture type edge and keep the `ProductPart` allowlist entry. A Logistics model lets the allowlist go empty. Follows existing `Contracts/Models` convention (`LogisticsCatalogItem`); DTO-like types are classes.

#### Decision 3: Adapter naming/visibility
`internal sealed class ManufactureGiftPackageBomAdapter` in `Features/Manufacture/Infrastructure/`, scoped, matching sibling adapters.

## Implementation Guidance

### Directory / Module Structure
- NEW `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IGiftPackageBomSource.cs`
- NEW `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsGiftPackagePart.cs`
- NEW `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureGiftPackageBomAdapter.cs`
- EDIT `Features/Manufacture/ManufactureModule.cs` (register)
- EDIT `Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- EDIT `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (empty `LogisticsAllowlist`, update its comment)
- EDIT tests: `Features/Logistics/GiftPackageManufactureServiceTests.cs`, `Features/Logistics/GiftPackageManufacture/GiftPackageManufactureAtomicityIntegrationTests.cs`; check `Application/GiftPackageManufacture/*Tests.cs` for service construction.
- NEW adapter unit test under `backend/test/Anela.Heblo.Tests/Features/Manufacture/`.

### Interfaces and Contracts
```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;
public interface IGiftPackageBomSource
{
    Task<List<LogisticsGiftPackagePart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default);
}
```
`LogisticsGiftPackagePart`: `string ProductCode`, `string ProductName`, `double Amount` (initialise strings to `string.Empty` to avoid nullable warnings).

### Data Flow
`GetGiftPackageDetailAsync` -> `IGiftPackageBomSource.GetSetPartsAsync` -> adapter -> `IManufactureClient.GetSetPartsAsync` -> map to `LogisticsGiftPackagePart` -> existing ingredient/stock mapping unchanged.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| DI resolution failure if registration missed (service is registered in Logistics module, adapter in Manufacture) | Medium | Register in `ManufactureModule`; existing module/DI tests (`GiftPackageManufactureModuleTests`) must still pass; verify they include Manufacture registration or add the mock |
| Boundary test fails on compiler-generated types | Low | Remove all Manufacture usings from the service; run `ModuleBoundariesTests` |
| Test constructors broken | Low | Update all `new GiftPackageManufactureService(...)` call sites (grep) |

## Specification Amendments
Contract placed in `Logistics/Contracts` (not `UseCases/.../Services`) with a Logistics-owned part model; allowlist entries for both `IManufactureClient` and `ProductPart` removed.

## Prerequisites
None. No migrations, config or infrastructure.
