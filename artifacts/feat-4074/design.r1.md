# Design: User Identity Resolution Fix for GiftPackageManufactureService

## Component Design

### `GiftPackageManufactureService` (`Services/GiftPackageManufactureService.cs`)
- **Responsibility change:** becomes identity-agnostic. No longer injects or references `ICurrentUserService`; no longer resolves "who is calling" — it only records whatever `userName` string it is given.
- Constructor: drop the `ICurrentUserService currentUserService` parameter and the `_currentUserService` field. Drop the `using Anela.Heblo.Domain.Features.Users;` import if nothing else in the file uses it.
- `CreateManufactureAsync(...)`: replace the internal `_currentUserService.GetCurrentUser().Name ?? "System"` call with the incoming `userName` parameter, used as-is (no further null-coalescing — the service performs no fallback logic) when constructing `GiftPackageManufactureLog`.
- `DisassembleGiftPackageAsync(...)`: same change, for the `GiftPackageOperationType.Disassembly` log construction.
- `GetAvailableGiftPackagesAsync` / `GetGiftPackageDetailAsync`: unchanged — they never touched identity.

### `IGiftPackageManufactureService` (`Services/IGiftPackageManufactureService.cs`)
- Contract owner for the two changed method signatures (below). `[DisplayName(...)]` attributes on both methods are left untouched — their `{0}`/`{1}` placeholders index `giftPackageCode`/`quantity`, which stay in the same positions.

### `CreateGiftPackageManufactureHandler` (`UseCases/CreateGiftPackageManufacture/CreateGiftPackageManufactureHandler.cs`)
- **Responsibility change:** becomes the identity-resolution boundary for this use case, per ADR-005. Gains a constructor dependency on `ICurrentUserService`.
- `Handle()`: calls `_currentUserService.GetCurrentUser()` exactly once, before invoking the service, and passes `user.Name ?? "System"` as the new `userName` argument. No other behavior changes.

### `DisassembleGiftPackageHandler` (`UseCases/DisassembleGiftPackage/DisassembleGiftPackageHandler.cs`)
- Same responsibility change as above: gains `ICurrentUserService`, resolves the user once at the top of `Handle()`, passes `user.Name ?? "System"` into `DisassembleGiftPackageAsync`.
- Existing `try`/`catch` for `InvalidOperationException`/`ArgumentException` around the service call is preserved unchanged; identity resolution happens before the service call regardless of whether it sits inside or outside that block.

### Contracts unaffected
`CreateGiftPackageManufactureRequest`/`Response`, `DisassembleGiftPackageRequest`/`Response`, their controller, and `GiftPackageManufactureModule.cs` DI registration are unchanged — this refactor is entirely internal to the Application-layer service/handler boundary and does not touch the HTTP contract or generated OpenAPI/TypeScript client.

## Data Schemas

No database, entity, or HTTP contract schema changes. Only the internal method signatures of `IGiftPackageManufactureService` change:

**`CreateManufactureAsync`**

Before:
```csharp
Task<GiftPackageManufactureDto> CreateManufactureAsync(
    string giftPackageCode,
    int quantity,
    bool allowStockOverride,
    CancellationToken cancellationToken = default);
```

After:
```csharp
Task<GiftPackageManufactureDto> CreateManufactureAsync(
    string giftPackageCode,
    int quantity,
    bool allowStockOverride,
    string userName,
    CancellationToken cancellationToken = default);
```

**`DisassembleGiftPackageAsync`**

Before:
```csharp
Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
    string giftPackageCode,
    int quantity,
    CancellationToken cancellationToken = default);
```

After:
```csharp
Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(
    string giftPackageCode,
    int quantity,
    string userName,
    CancellationToken cancellationToken = default);
```

`userName` is inserted immediately after the last existing business parameter and immediately before the trailing `CancellationToken cancellationToken = default`, matching this interface's existing flat-scalar-parameter style.

**Domain entity (unchanged):** `GiftPackageManufactureLog` already accepts a `createdBy` string in its constructor(s); this change only alters where that value originates (handler-resolved `userName` instead of a service-internal `ICurrentUserService` call). `GiftPackageManufactureDto.CreatedBy` and `GiftPackageDisassemblyDto.DisassembledBy` continue to be populated the same way, just sourced from the new parameter.

**Constructor shapes:**

```csharp
// GiftPackageManufactureService — removes ICurrentUserService
public GiftPackageManufactureService(/* existing deps minus ICurrentUserService */)

// CreateGiftPackageManufactureHandler — adds ICurrentUserService
public CreateGiftPackageManufactureHandler(
    IGiftPackageManufactureService giftPackageService,
    ICurrentUserService currentUserService)

// DisassembleGiftPackageHandler — adds ICurrentUserService
public DisassembleGiftPackageHandler(
    IGiftPackageManufactureService giftPackageService,
    ICurrentUserService currentUserService,
    /* existing deps unchanged */)
```

No new DI registration is required: `ICurrentUserService` is already registered by `UsersModule.AddUsersModule()` and resolvable by any handler via standard constructor injection.
