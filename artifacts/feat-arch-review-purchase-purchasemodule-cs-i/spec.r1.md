# Specification: Restore Clean Architecture Boundary in PurchaseModule DI Registration

## Summary
The Application-layer `PurchaseModule.cs` currently imports and directly instantiates Persistence-layer types (`ApplicationDbContext`, `PurchaseOrderRepository`) to wire up `IPurchaseOrderRepository`. This violates the Clean Architecture dependency rule (Application must not depend on Infrastructure/Persistence). The fix moves the `IPurchaseOrderRepository` → `PurchaseOrderRepository` DI registration into `PersistenceModule.cs` and removes the offending `using Anela.Heblo.Persistence*` imports from `PurchaseModule.cs`.

## Background
The codebase follows Clean Architecture with four layers: Domain → Application → Persistence/Infrastructure → API. Dependencies must flow inward: Persistence implements interfaces defined in Domain/Application, and the composition root (API layer via `AddPersistenceServices` + `AddApplicationServices`) wires them together.

`backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs:7-23` breaks this rule:

```csharp
using Anela.Heblo.Persistence;                          // Persistence namespace
using Anela.Heblo.Persistence.Purchase.PurchaseOrders;  // Persistence namespace
...
services.AddScoped<IPurchaseOrderRepository>(provider =>
{
    var context = provider.GetRequiredService<ApplicationDbContext>();
    return new PurchaseOrderRepository(context);
});
```

The Application project references the Persistence project to compile, making Application logic untestable without the Persistence assembly and defeating the purpose of the `IPurchaseOrderRepository` abstraction.

All other repository registrations already live in `PersistenceModule.cs` (`backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:127-163`) — `IUserDashboardSettingsRepository`, `IBankStatementImportRepository`, `IArticleRepository`, etc. The Purchase repository is the deviation.

This finding was filed by the daily arch-review routine on 2026-05-22.

## Functional Requirements

### FR-1: Move `IPurchaseOrderRepository` registration to PersistenceModule
The factory registration that binds `IPurchaseOrderRepository` to `PurchaseOrderRepository` (resolving `ApplicationDbContext` via the provider) must be relocated from `PurchaseModule.AddPurchaseModule` to `PersistenceModule.AddPersistenceServices`.

The registration in `PersistenceModule.cs` should follow the same pattern used by the surrounding repository registrations (concrete-class registration; the factory lambda is no longer required because `PurchaseOrderRepository` and `ApplicationDbContext` are now in the same assembly):

```csharp
// Purchase repositories
services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
```

**Acceptance criteria:**
- `IPurchaseOrderRepository` is registered exactly once at composition time, inside `AddPersistenceServices` in `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`.
- The scope (`Scoped`) is preserved.
- `PurchaseModule.cs` no longer contains any registration for `IPurchaseOrderRepository`.
- The registration appears in a section commented `// Purchase repositories` (matching the existing style for other modules in `PersistenceModule.cs`).

### FR-2: Remove Persistence imports from PurchaseModule.cs
After FR-1, the `PurchaseModule.cs` file must have no references to types in `Anela.Heblo.Persistence.*` namespaces.

**Acceptance criteria:**
- `using Anela.Heblo.Persistence;` is removed from `PurchaseModule.cs`.
- `using Anela.Heblo.Persistence.Purchase.PurchaseOrders;` is removed from `PurchaseModule.cs`.
- No other `Anela.Heblo.Persistence*` `using` directive is introduced.
- `grep -n "Anela\.Heblo\.Persistence" backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` returns no matches.

### FR-3: Preserve all other PurchaseModule registrations
The remaining registrations in `PurchaseModule.AddPurchaseModule` must continue to function unchanged. These belong in the Application layer because they reference Application/Domain types only:

- `IPurchaseOrderNumberGenerator` → `PurchaseOrderNumberGenerator` (Domain type)
- `IStockSeverityCalculator` → `StockSeverityCalculator` (Application service)
- `IValidator<CreatePurchaseOrderRequest>` → `CreatePurchaseOrderRequestValidator`
- `IValidator<UpdatePurchaseOrderRequest>` → `UpdatePurchaseOrderRequestValidator`
- `LowStockEfficiencyTile` via `RegisterTile<>`

**Acceptance criteria:**
- All five registrations above remain in `PurchaseModule.cs` with identical lifetimes and target types.
- `PurchaseModule.AddPurchaseModule` continues to return `IServiceCollection` and is still invoked from `ApplicationModule.cs:78`.

### FR-4: Preserve runtime behavior end-to-end
The end-to-end Purchase Order flow (create / read / update / list / status-change) must behave identically before and after the change. This is a pure DI relocation, not a behavioral change.

**Acceptance criteria:**
- Application starts under both Development and Production configurations without DI validation errors.
- DI validation (`ValidateScopes`/`ValidateOnBuild` as currently configured) does not report a missing or duplicate `IPurchaseOrderRepository` registration.
- All existing tests in `backend/test/Anela.Heblo.Tests/Features/Purchase/**` and `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs` continue to pass without modification.
- The integration test factory `PurchaseOrdersTestFactory` (which currently relies on the default repository registration coming from the host) still resolves `IPurchaseOrderRepository` against the EF Core in-memory `ApplicationDbContext`.

### FR-5: Composition root invocation order remains valid
`API/Program.cs:74` calls `AddPersistenceServices` before `AddApplicationServices` (which calls `AddPurchaseModule`). The new registration location must remain compatible with this order. Since `PurchaseModule` no longer registers `IPurchaseOrderRepository`, no ordering coupling is introduced.

**Acceptance criteria:**
- No change required in `Program.cs` invocation order.
- The Application project's reference to the Persistence project may remain (other application modules still reference Persistence; that broader violation is out of scope — see "Out of Scope"). What matters is that `PurchaseModule.cs` itself no longer imports Persistence namespaces.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. The relocation changes only the DI registration site; resolution semantics, lifetime (`Scoped`), and instantiation cost are unchanged.

### NFR-2: Security
No security impact. No auth surface, configuration, secret handling, or data validation is modified.

### NFR-3: Build & Static Analysis
- `dotnet build` for the full solution must succeed with no new warnings or errors.
- `dotnet format` must report no formatting drift on the modified files.
- No new analyzer suppressions are introduced.

### NFR-4: Testability
After the change, the Application project's compilation does not require any reference to a concrete `ApplicationDbContext` for the Purchase feature wiring. Unit tests targeting Application/Purchase handlers already mock `IPurchaseOrderRepository`; this NFR captures that the abstraction is intact.

## Data Model
Not affected. No schema, entity, migration, or column change. The `PurchaseOrder` entity, its `Lines` and `History`, and `ApplicationDbContext`'s `DbSet<PurchaseOrder>` configuration remain as-is.

## API / Interface Design
Not affected. No HTTP endpoint, request/response DTO, MediatR contract, or public service signature changes. The change is purely an internal DI composition refactor.

## Dependencies
- Existing types touched (read-only references): `Anela.Heblo.Domain.Features.Purchase.IPurchaseOrderRepository`, `Anela.Heblo.Persistence.Purchase.PurchaseOrders.PurchaseOrderRepository`, `Anela.Heblo.Persistence.ApplicationDbContext`.
- Files modified:
  - `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` (remove registration + imports)
  - `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` (add registration)
- Tests exercised (no modifications expected):
  - `backend/test/Anela.Heblo.Tests/Features/Purchase/*`
  - `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs`
  - `backend/test/Anela.Heblo.Tests/Persistence/PersistenceModuleTests.cs`

## Out of Scope
- **Other Application modules with the same violation.** A grep shows ~20 other files in `backend/src/Anela.Heblo.Application/**` that `using Anela.Heblo.Persistence*` (e.g. `SmartsuppModule.cs`, `PhotobankModule.cs`, `CatalogModule.cs`, `ManufactureModule.cs`, etc.). Those are not in this brief and must not be touched. Each will be addressed by its own arch-review finding.
- **Removing the Application → Persistence project reference.** Until every Application module is cleaned up, the project-level reference cannot be removed. That is a follow-up only after all per-module fixes land.
- **Refactoring `PurchaseOrderRepository` itself**, `BaseRepository<T,TKey>`, or any of its methods.
- **Changing `IPurchaseOrderNumberGenerator`, `IStockSeverityCalculator`, or any validator** — these correctly stay in `PurchaseModule.cs`.
- **Changing the EAN code generator branching** or any other unrelated `PersistenceModule.cs` logic.
- **Integration test infrastructure** (`HebloWebApplicationFactory`, `PurchaseOrdersTestFactory`) — these are expected to keep working unchanged.

## Open Questions
None.

## Status: COMPLETE
