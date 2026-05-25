I have full context now. Generating the architecture review.

```markdown
# Architecture Review: Restore Clean Architecture Boundary in PurchaseModule DI Registration

## Skip Design: true

No UI/UX work — pure backend DI relocation. No visual components, screens, layouts, or HTTP contracts change.

## Architectural Fit Assessment

The proposed change is a direct, surgical correction that brings `PurchaseModule.cs` into line with patterns already in force across the codebase. Verification against the actual code confirms every assumption in the spec:

- **Pattern already established.** `PersistenceModule.cs:127-163` registers 13 other repository pairs as `services.AddScoped<IFoo, FooRepository>()` (one-liner, no factory lambda). The Purchase repo is the sole deviation.
- **Composition order is safe.** `API/Program.cs` calls `AddPersistenceServices` before `AddApplicationServices` → `AddPurchaseModule`. Whether `IPurchaseOrderRepository` is registered in the Persistence call or the Application call, both fire before any handler resolves it. There is no ordering coupling.
- **DI validation is enforced in tests.** `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices` runs `ValidateOnBuild = true; ValidateScopes = true` against the real `Program` graph. Any duplicate or missing registration will fail CI immediately. This is the strongest possible safety net for this refactor.
- **Test override seam is preserved.** `PurchaseOrdersTestFactory` (test file lines 564-585) does not override `IPurchaseOrderRepository`; it relies on the host's default registration plus the in-memory `ApplicationDbContext`. Moving the registration to `PersistenceModule` keeps that seam working because `AddPersistenceServices` runs first in the test host as well.
- **The `InMemoryPurchaseOrderRepository` (Application/Features/Purchase/Services) is not currently DI-registered** — `grep` confirms it is referenced only inside tests, not as a default. The Persistence-layer EF repository is genuinely the only production binding, so relocation does not silently swap behavior.

The change has zero behavioral surface and high architectural value. Recommend proceeding exactly as the spec states.

## Proposed Architecture

### Component Overview

```
Before (violation):

  API/Program.cs
       │
       ├─► AddPersistenceServices()  → registers ApplicationDbContext + 13 repos
       │
       └─► AddApplicationServices()
              └─► AddPurchaseModule()
                     │   imports Anela.Heblo.Persistence       ← VIOLATION
                     │   imports Anela.Heblo.Persistence.Purchase.PurchaseOrders
                     ├── IPurchaseOrderRepository → factory(provider) new PurchaseOrderRepository(ctx)
                     ├── IPurchaseOrderNumberGenerator → PurchaseOrderNumberGenerator
                     ├── IStockSeverityCalculator → StockSeverityCalculator
                     ├── IValidator<Create/Update PurchaseOrderRequest>
                     └── RegisterTile<LowStockEfficiencyTile>

After (clean):

  API/Program.cs
       │
       ├─► AddPersistenceServices()
       │      └── …
       │      └── // Purchase repositories
       │            services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
       │
       └─► AddApplicationServices()
              └─► AddPurchaseModule()              ← no Persistence imports
                     ├── IPurchaseOrderNumberGenerator → PurchaseOrderNumberGenerator
                     ├── IStockSeverityCalculator → StockSeverityCalculator
                     ├── IValidator<Create/Update PurchaseOrderRequest>
                     └── RegisterTile<LowStockEfficiencyTile>
```

The dependency arrow `Application → Persistence` for *this module's wiring* is eliminated. The project-level reference remains (other modules still use it; out of scope per spec).

### Key Design Decisions

#### Decision 1: Use concrete-type registration, not factory lambda
**Options considered:**
- (A) Port the existing factory `provider => new PurchaseOrderRepository(provider.GetRequiredService<ApplicationDbContext>())` verbatim into `PersistenceModule`.
- (B) Collapse to the conventional `services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>()`.

**Chosen approach:** (B).

**Rationale:** Once both `PurchaseOrderRepository` and `ApplicationDbContext` live in the same assembly, the factory adds no value — DI will satisfy the constructor parameter via normal resolution. Every other repo in `PersistenceModule` (lines 128-163) uses the one-liner. Matching the established pattern improves readability and removes a needless closure.

#### Decision 2: Where in `PersistenceModule` to place the registration
**Options considered:**
- (A) At the end of the repository block (after Feature Flags).
- (B) Grouped by module, ordered alphabetically.
- (C) In a new `// Purchase repositories` comment block, slotted by feature grouping similar to neighboring sections (Bank, Stock, KnowledgeBase, etc.).

**Chosen approach:** (C). Insert immediately after one of the existing single-line module groups (e.g., after "Background Jobs repositories" or "Stock repositories"). The spec mandates the `// Purchase repositories` comment header.

**Rationale:** The existing file organizes registrations by feature module with a one-line comment header per group. Following that convention keeps the file self-consistent and discoverable for future module cleanups.

#### Decision 3: Do not touch `InMemoryPurchaseOrderRepository`
**Options considered:**
- (A) Delete the unused class while editing the area.
- (B) Leave it untouched.

**Chosen approach:** (B).

**Rationale:** It is not in this brief's scope, `CLAUDE.md` mandates surgical changes, and the class may be intended for future test wiring. If it is genuinely dead, it should be removed by a separate cleanup finding, not bundled here.

## Implementation Guidance

### Directory / Module Structure

No new files. Two existing files modified:

```
backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs  (edit: remove 2 usings + 5 lines of registration)
backend/src/Anela.Heblo.Persistence/PersistenceModule.cs                 (edit: add 2 lines — comment + registration)
```

### Interfaces and Contracts

No interface or contract changes. The contract `IPurchaseOrderRepository` already lives in `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs` (correct location — Domain layer) and is unchanged. `PurchaseOrderRepository` constructor signature `(ApplicationDbContext context)` is unchanged.

### Data Flow

Resolution path is identical to today's behavior:

```
Handler (e.g. CreatePurchaseOrderHandler)
   ├─ ctor: IPurchaseOrderRepository repo  ──► resolved by DI
                                                  │
                                                  ▼
                                           PurchaseOrderRepository
                                                  │
                                                  ▼  ctor: ApplicationDbContext context
                                           ApplicationDbContext (Scoped)
                                                  │
                                                  ▼  uses
                                           NpgsqlDataSource (Singleton, in non-InMemory mode)
```

Only the *registration site* changes. Resolution graph, lifetimes (Scoped for both repo and context), and DbContext options (NpgsqlDataSource singleton + interceptors) are untouched.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Duplicate `IPurchaseOrderRepository` registration if old line is not removed | High | `CompositionRootTests.ValidateOnBuild` will not fail on duplicates per se, but a manual `grep -rn "IPurchaseOrderRepository" backend/src/**/*.cs` after the edit will confirm exactly one registration site. Also: `PurchaseOrdersControllerTests` runtime resolution would surface any mismatch immediately. |
| `PurchaseModule.cs` accidentally left with a stale `using Anela.Heblo.Persistence*` directive | Medium | Spec-mandated verification: `grep -n "Anela\.Heblo\.Persistence" backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` must return no matches. `dotnet format` will also flag unused usings. |
| Test factory `PurchaseOrdersTestFactory` silently loses its repository binding | Low | The factory does not override `IPurchaseOrderRepository`; it relies on the host registration. Since `AddPersistenceServices` runs first in both `Program.cs` and `HebloWebApplicationFactory`, the binding remains. `PurchaseOrdersControllerTests` CRUD coverage will catch any breakage. |
| Hidden consumer in `InMemoryPurchaseOrderRepository` or elsewhere that assumed registration from `PurchaseModule` | Low | `grep` confirms no production code registers or references `InMemoryPurchaseOrderRepository`. No risk. |
| Future modules following the wrong pattern by copying old `PurchaseModule.cs` | Low | The cleanup itself removes the bad exemplar. Other ~20 violations are tracked separately per the spec's Out of Scope. |

## Specification Amendments

None required. The spec is precise, scoped tightly, and matches the actual codebase verbatim. The few minor refinements worth tracking during implementation but not changing the spec:

1. **Recommended placement** in `PersistenceModule.cs`: insert after line 141 (`// Background Jobs repositories` block) or after line 138 (`// Stock repositories` block) — either keeps related domain groupings adjacent. The spec does not pin a line and should not.
2. **Verification command** for FR-2 in spec is correct; suggest also running `dotnet format --verify-no-changes` on `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` to catch any leftover blank-line drift after the using removal.

## Prerequisites

None. No migrations, configuration, infrastructure, or upstream PRs are required. The change is self-contained and runs against the existing `dotnet build` + `dotnet test` pipeline. Validation gate is `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices` plus the existing `PurchaseOrdersControllerTests` suite — both already in CI.
```