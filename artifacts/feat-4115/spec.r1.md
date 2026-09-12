# Specification: Split IGiftPackageManufactureService into query and command interfaces

## Summary
`IGiftPackageManufactureService` in the Logistics module currently bundles two read operations and two write operations behind one interface, violating the Interface Segregation Principle. This change splits it into `IGiftPackageQueryService` (reads) and a slimmed-down `IGiftPackageManufactureService` (writes only), updates the two query handlers to depend only on the query interface, and keeps a single concrete class implementing both so existing behavior, DI wiring, and callers of the write handlers are unaffected.

## Background
The interface is defined at `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageManufactureService.cs` and implemented by `GiftPackageManufactureService` in the same folder. It has four members:

- `GetAvailableGiftPackagesAsync` (read) — used only by `GetAvailableGiftPackagesHandler`
- `GetGiftPackageDetailAsync` (read) — used by `GetGiftPackageDetailHandler`, and also called internally by the implementation itself from `CreateManufactureAsync` and `DisassembleGiftPackageAsync` to fetch the ingredient BOM before posting stock operations
- `CreateManufactureAsync` (write) — used only by `CreateGiftPackageManufactureHandler`
- `DisassembleGiftPackageAsync` (write) — used only by `DisassembleGiftPackageHandler`

Confirmed via a workspace-wide search: `IGiftPackageManufactureService` is referenced in exactly four production consumers (the two query handlers, the two command handlers) plus the DI registration (`GiftPackageManufactureModule.cs`) and two existing unit-test files (`CreateGiftPackageManufactureHandlerTests.cs`, `DisassembleGiftPackageHandlerTests.cs`). There are no other consumers, so the interface is safe to split without touching any code outside this module.

This matches the project's own documented guideline in `docs/architecture/development_guidelines.md` ("Consumer (A) defines the contract... exposing only the operations it actually consumes (no speculative methods)"), so this change brings the module in line with an already-established, documented convention rather than introducing a new one.

Today, `GetAvailableGiftPackagesHandler` and `GetGiftPackageDetailHandler` inject the full interface and therefore have compile-time access to `CreateManufactureAsync` and `DisassembleGiftPackageAsync` even though they never call them. This inflates the mock surface in any test for those handlers, obscures — for a reader auditing where writes happen — that a query handler could invoke a write, and mixes two different concerns (inventory/BOM lookup vs. stock-mutating manufacture operations) under one name.

## Functional Requirements

### FR-1: Introduce `IGiftPackageQueryService` with the two read methods
Create a new interface `IGiftPackageQueryService` in the same `Services` folder (`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/IGiftPackageQueryService.cs`), containing exactly the two read methods, with identical signatures (including default parameter values) to what they are today on `IGiftPackageManufactureService`:

```csharp
Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
```

**Acceptance criteria:**
- `IGiftPackageQueryService` exists with exactly these two method signatures, unchanged from the current interface (parameter names, order, and defaults preserved).
- No write method (`CreateManufactureAsync`, `DisassembleGiftPackageAsync`) is reachable through `IGiftPackageQueryService`.

### FR-2: Reduce `IGiftPackageManufactureService` to the two write methods
Remove `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` from `IGiftPackageManufactureService`, leaving only:

```csharp
[DisplayName("GiftPackageManufacture-{0}-{1}x")]
Task<GiftPackageManufactureDto> CreateManufactureAsync(string giftPackageCode, int quantity, bool allowStockOverride, string userName, CancellationToken cancellationToken = default);

Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(string giftPackageCode, int quantity, string userName, CancellationToken cancellationToken = default);
```

**Acceptance criteria:**
- `IGiftPackageManufactureService` contains only the two write methods; no read method is present.
- The `[DisplayName("GiftPackageManufacture-{0}-{1}x")]` attribute on `CreateManufactureAsync` is preserved exactly as-is on the interface method (unrelated pre-existing text mismatch with the `[DisplayName("GiftPackageManufacture-{0}-{1}")]` attribute on the implementation is out of scope for this change and must not be "fixed" incidentally).

### FR-3: `GiftPackageManufactureService` implements both interfaces
`GiftPackageManufactureService` becomes:

```csharp
public class GiftPackageManufactureService : IGiftPackageManufactureService, IGiftPackageQueryService
```

All four method bodies (`GetAvailableGiftPackagesAsync`, `GetGiftPackageDetailAsync`, `CreateManufactureAsync`, `DisassembleGiftPackageAsync`) and all private helper methods (`ResolveDateRange`, `ComputePackageMetrics`, `CalculateSeverity`, `CalculateStockCoveragePercent`) remain in this single class, unchanged, in the same file. This is a pure interface-shape refactor — no behavioral logic changes.

The internal calls from `CreateManufactureAsync` and `DisassembleGiftPackageAsync` to `GetGiftPackageDetailAsync` (used to fetch the BOM/ingredients before posting stock operations) continue to work as plain `this.GetGiftPackageDetailAsync(...)` calls within the same class — they do not need to go through an injected `IGiftPackageQueryService` reference, since the method is already on `this`.

**Acceptance criteria:**
- `GiftPackageManufactureService` compiles implementing both interfaces with no duplicated logic.
- `CreateManufactureAsync` and `DisassembleGiftPackageAsync` behave identically to today (same stock operations, same document-number formats, same log entries) — verified by the existing handler-level unit tests continuing to pass unmodified in assertions (only their mock type changes, per FR-5).

### FR-4: Update DI registration
In `GiftPackageManufactureModule.cs`, register the concrete `GiftPackageManufactureService` against both interfaces so both resolve to the same implementation type:

```csharp
services.AddScoped<IGiftPackageManufactureService, GiftPackageManufactureService>();
services.AddScoped<IGiftPackageQueryService>(provider => (IGiftPackageQueryService)provider.GetRequiredService<IGiftPackageManufactureService>());
```

(Exact registration style — e.g. registering the concrete type once as scoped and mapping both interfaces to it via factory delegates, versus two independent `AddScoped` calls that each construct a new instance — is left to the architect/implementer, but **must guarantee that within one scope both interfaces resolve to the same `GiftPackageManufactureService` instance**, matching today's implicit behavior where callers of read and write operations shared exactly one service instance per DI scope.)

**Acceptance criteria:**
- After registration, resolving `IGiftPackageManufactureService` and `IGiftPackageQueryService` from the same DI scope returns the same object reference.
- The application starts and all four use cases (`GetAvailableGiftPackages`, `GetGiftPackageDetail`, `CreateGiftPackageManufacture`, `DisassembleGiftPackage`) function through DI exactly as before.

### FR-5: Update query handlers to depend on the narrower interface
- `GetAvailableGiftPackagesHandler` — change its constructor dependency from `IGiftPackageManufactureService` to `IGiftPackageQueryService`; update the injected field type and the one call site (`GetAvailableGiftPackagesAsync`) accordingly.
- `GetGiftPackageDetailHandler` — same change, for `GetGiftPackageDetailAsync`.

`CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` keep depending on `IGiftPackageManufactureService` (now the write-only interface) — no change to these two handlers' dependency type, since both only call write methods that remain on that interface.

**Acceptance criteria:**
- `GetAvailableGiftPackagesHandler` and `GetGiftPackageDetailHandler` no longer reference `IGiftPackageManufactureService` anywhere in their source.
- `CreateGiftPackageManufactureHandler` and `DisassembleGiftPackageHandler` are unchanged (they already only ever called write methods, so their existing dependency on `IGiftPackageManufactureService` remains valid against the reduced interface with no source changes required — confirmed by direct reading of both handler files).
- All four handlers' MediatR request/response contracts (`GetAvailableGiftPackagesRequest/Response`, `GetGiftPackageDetailRequest/Response`, `CreateGiftPackageManufactureRequest/Response`, `DisassembleGiftPackageRequest/Response`) are untouched.

### FR-6: Update existing unit tests to the new interface split
The two existing test files under `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/` currently mock `IGiftPackageManufactureService` even though each only exercises a write method:

- `CreateGiftPackageManufactureHandlerTests.cs` mocks `s.CreateManufactureAsync(...)` — no change needed to which interface it mocks, since `CreateGiftPackageManufactureHandler` still depends on `IGiftPackageManufactureService`.
- `DisassembleGiftPackageHandlerTests.cs` mocks `s.DisassembleGiftPackageAsync(...)` — likewise unaffected, since `DisassembleGiftPackageHandler` still depends on `IGiftPackageManufactureService`.

No handler-level tests currently exist for `GetAvailableGiftPackagesHandler` or `GetGiftPackageDetailHandler` (none were found in the module's test folder), so there are no existing tests to migrate to `IGiftPackageQueryService` mocks. If the implementer chooses to add such tests as part of this change, they should mock `IGiftPackageQueryService`, not `IGiftPackageManufactureService` — but adding new tests is not required to satisfy this refactor's acceptance criteria (see Out of Scope).

**Acceptance criteria:**
- `CreateGiftPackageManufactureHandlerTests.cs` and `DisassembleGiftPackageHandlerTests.cs` compile and pass without modification (they already target the write interface, whose shape is unaffected for their purposes).
- No test file references a mix of read and write methods on the same mocked interface instance after the split.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — this is a compile-time interface reshaping with no change to runtime call paths, database access patterns, or the number of service instances created per request (see FR-4 on shared-instance registration).

### NFR-2: Security
No change. No new attack surface, no change to authorization, no change to what data flows where. `ICurrentUserService` usage in the two command handlers is untouched.

### NFR-3: Maintainability
Handlers now have constructor dependencies that precisely match their actual capability needs, so a reviewer can determine from a handler's constructor alone whether it can perform a write. Unit tests for query handlers (if/when added) only need to mock the two-method `IGiftPackageQueryService`, not the full four-method surface.

## Data Model
No data model changes. This is a code-organization refactor only; no entities, DTOs, or persistence schema are added, removed, or modified. `GiftPackageManufactureLog`, `GiftPackageManufactureItem`, and all `Contracts/*Dto.cs` types are untouched.

## API / Interface Design

**Before:**
```
IGiftPackageManufactureService
├── GetAvailableGiftPackagesAsync   (read)
├── GetGiftPackageDetailAsync       (read)
├── CreateManufactureAsync          (write)
└── DisassembleGiftPackageAsync     (write)
```

**After:**
```
IGiftPackageQueryService                    IGiftPackageManufactureService
├── GetAvailableGiftPackagesAsync           ├── CreateManufactureAsync
└── GetGiftPackageDetailAsync               └── DisassembleGiftPackageAsync
        ▲                                            ▲
        │                                            │
        └──────────────┬─────────────────────────────┘
                        │
              GiftPackageManufactureService
              (implements both; single class,
               single DI-scoped instance)
```

Handler → interface dependency after the change:

| Handler | Depends on |
|---|---|
| `GetAvailableGiftPackagesHandler` | `IGiftPackageQueryService` |
| `GetGiftPackageDetailHandler` | `IGiftPackageQueryService` |
| `CreateGiftPackageManufactureHandler` | `IGiftPackageManufactureService` |
| `DisassembleGiftPackageHandler` | `IGiftPackageManufactureService` |
| `GetManufactureLogHandler` | *(unaffected — depends directly on `IGiftPackageManufactureRepository`, not this service)* |

No MediatR request/response contracts, no controller routes, and no HTTP-facing API surface change as a result of this refactor — it is entirely internal to the Application layer's Logistics module.

## Dependencies
- No new external libraries or services.
- No change to `IManufactureClient`, `IGiftPackageManufactureRepository`, `ILogisticsCatalogSource`, `ILogisticsStockOperationService`, `IMapper`, or `TimeProvider` — all remain injected into `GiftPackageManufactureService` exactly as today.
- Depends on the project's DI container (Microsoft.Extensions.DependencyInjection) supporting a factory-based registration that maps a second interface to an already-registered scoped instance (FR-4) — this is a standard, already-used pattern elsewhere in this codebase (e.g. the repository factory registration already present in `GiftPackageManufactureModule.cs`).

## Out of Scope
- Any behavioral change to the manufacture or disassembly logic, stock-operation document-number formats, or BOM/ingredient resolution.
- Fixing the pre-existing text mismatch between the `[DisplayName]` attribute value on the interface (`"...{1}x"`) and on the implementation (`"...{1}"`) — flagged here for visibility but not part of this change per the "surgical changes" project rule.
- Adding new unit tests for `GetAvailableGiftPackagesHandler` or `GetGiftPackageDetailHandler` (none exist today; none are required to satisfy this refactor).
- Splitting `IGiftPackageManufactureRepository` or any other interface in this module — this spec addresses only `IGiftPackageManufactureService` as identified in the brief.
- Renaming the `GiftPackageManufactureService` class itself, or moving it to a different file/folder.
- Any change to the `GetManufactureLogHandler` use case — it does not depend on either interface being split.

## Open Questions
None.

## Status: COMPLETE
