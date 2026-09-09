# Design: Split IGiftPackageManufactureService into query and command interfaces

## Component Design

### `IGiftPackageQueryService` (new)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageQueryService.cs`
- **Namespace:** `Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services`
- **Responsibility:** Read-only access to gift package availability and detail data. No stock-mutating capability is reachable through this interface.
- **Members:**
  - `GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default) : Task<List<GiftPackageDto>>`
  - `GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default) : Task<GiftPackageDto>`
- **Consumers:** `GetAvailableGiftPackagesHandler`, `GetGiftPackageDetailHandler`.

### `IGiftPackageManufactureService` (narrowed, existing file)
- **Location:** unchanged — `.../GiftPackageManufacture/Services/IGiftPackageManufactureService.cs`
- **Responsibility:** Stock-mutating manufacture and disassembly operations only. No read method is present.
- **Members:**
  - `[DisplayName("GiftPackageManufacture-{0}-{1}x")] CreateManufactureAsync(string giftPackageCode, int quantity, bool allowStockOverride, string userName, CancellationToken cancellationToken = default) : Task<GiftPackageManufactureDto>`
  - `DisassembleGiftPackageAsync(string giftPackageCode, int quantity, string userName, CancellationToken cancellationToken = default) : Task<GiftPackageDisassemblyDto>`
- **Consumers:** `CreateGiftPackageManufactureHandler`, `DisassembleGiftPackageHandler`.
- The pre-existing `[DisplayName]` text mismatch against the implementation (`"...{1}x"` vs `"...{1}"`) is preserved verbatim — not touched by this change.

### `GiftPackageManufactureService` (existing file, class declaration only changes)
- **Location:** unchanged — `.../GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- **Declaration:** `public class GiftPackageManufactureService : IGiftPackageManufactureService, IGiftPackageQueryService`
- **Responsibility:** Single concrete implementation of both the query and write contracts. Owns all four method bodies and all private helpers (`ResolveDateRange`, `ComputePackageMetrics`, `CalculateSeverity`, `CalculateStockCoveragePercent`) unchanged.
- **Internal composition:** `CreateManufactureAsync` and `DisassembleGiftPackageAsync` continue to call `GetGiftPackageDetailAsync` as a plain `this.` call — no self-injection, no behavior change. This is a pure interface-shape refactor; the constructor's injected dependencies (`IManufactureClient`, `IGiftPackageManufactureRepository`, `ILogisticsCatalogSource`, `ILogisticsStockOperationService`, `IMapper`, `TimeProvider`) are untouched.

### Handlers
| Handler | Dependency after change | Change scope |
|---|---|---|
| `GetAvailableGiftPackagesHandler` | `IGiftPackageQueryService` | constructor param type + field type + call site |
| `GetGiftPackageDetailHandler` | `IGiftPackageQueryService` | constructor param type + field type + call site |
| `CreateGiftPackageManufactureHandler` | `IGiftPackageManufactureService` (unchanged, now write-only) | none |
| `DisassembleGiftPackageHandler` | `IGiftPackageManufactureService` (unchanged, now write-only) | none |
| `GetManufactureLogHandler` | `IGiftPackageManufactureRepository` (unaffected) | none |

### `GiftPackageManufactureModule` (DI composition root)
- **Location:** `.../GiftPackageManufacture/GiftPackageManufactureModule.cs`
- **Responsibility:** Register the concrete `GiftPackageManufactureService` as the single scoped root, then alias both interfaces to that same registration so any resolution of either interface within one DI scope returns the identical instance:

```csharp
services.AddScoped<GiftPackageManufactureService>();
services.AddScoped<IGiftPackageManufactureService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
services.AddScoped<IGiftPackageQueryService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
```

This is a symmetric alias of a concrete-type root registration (no runtime cast, no interface-to-interface dependency), matching the arch review's Decision 2.

## Data Schemas

No persistence, entity, DTO, or HTTP contract changes. `GiftPackageManufactureLog`, `GiftPackageManufactureItem`, and all `Contracts/*Dto.cs` types (`GiftPackageDto`, `GiftPackageManufactureDto`, `GiftPackageDisassemblyDto`) are unmodified. This section documents only the interface (in-process contract) shapes introduced or changed.

### `IGiftPackageQueryService` (new contract shape)

```csharp
public interface IGiftPackageQueryService
{
    Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(
        decimal salesCoefficient = 1.0m,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    Task<GiftPackageDto> GetGiftPackageDetailAsync(
        string giftPackageCode,
        decimal salesCoefficient = 1.0m,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
}
```

### `IGiftPackageManufactureService` (reduced contract shape)

```csharp
public interface IGiftPackageManufactureService
{
    [DisplayName("GiftPackageManufacture-{0}-{1}x")]
    Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride,
        string userName,
        CancellationToken cancellationToken = default);

    Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
        string giftPackageCode,
        int quantity,
        string userName,
        CancellationToken cancellationToken = default);
}
```

### DI resolution contract (behavioral schema, not a data shape, but the one new invariant this change introduces)

Within a single DI scope:

```
scope.GetRequiredService<IGiftPackageManufactureService>()
  == (by reference)
scope.GetRequiredService<IGiftPackageQueryService>()
  == (by reference)
scope.GetRequiredService<GiftPackageManufactureService>()
```

No MediatR request/response contracts, no controller routes, and no HTTP-facing API surface change as a result of this refactor.
