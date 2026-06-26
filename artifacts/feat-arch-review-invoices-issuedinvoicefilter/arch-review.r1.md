I have enough context. Let me write the architecture review.

```markdown
# Architecture Review: Relocate `IssuedInvoiceFilters` and `PaginatedResult<T>` out of Domain

## Skip Design: true

No UI, visual components, or new API surface. Pure structural refactor of internal C# types.

## Architectural Fit Assessment

The spec is correct that `IssuedInvoiceFilters` (with `PageNumber`, `PageSize`, `SortBy`, `ShowOnlyUnsynced`) and `PaginatedResult<T>` are application-level concepts wrongly placed in Domain. The fix aligns with `docs/architecture/development_guidelines.md` (Domain is for entities, value objects, and domain-meaningful repository contracts).

**Critical finding that contradicts the spec's FR-3a premise.** The spec says: *"The Infrastructure/persistence project that implements it already depends on Application."* This is **false** in this codebase. Verified project references:

- `Anela.Heblo.Application` → `Domain`, `Persistence`, `Xcc`
- `Anela.Heblo.Persistence` → `Domain`, `Xcc` (does **not** reference Application)
- `Anela.Heblo.Domain` → `Xcc`

The dependency direction is **inverted** from textbook Clean Architecture — Application references Persistence (not the other way round). There is even an explicit comment in `Application/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs`:

> *Interface lives in Domain to avoid circular dependency (Application → Persistence → Application). Re-exported here so Application use cases can reference the canonical namespace.*

Consequently, the spec's **Option A as worded is not viable** if the implementation stays in Persistence: moving `IIssuedInvoiceRepository` to Application would force `Persistence/Invoices/IssuedInvoiceRepository.cs` to reference Application, creating a circular project reference.

The good news: there is **an existing precedent** for repository interfaces living in Application (`Application/Features/Purchase/ISupplierRepository.cs`, `Application/Features/FeatureFlags/IFeatureFlagOverrideRepository.cs` as alias). And `InvoicesModule.cs` already registers `IIssuedInvoiceRepository` from inside Application (`Application/Features/Invoices/InvoicesModule.cs:20`), so the DI wiring is already where it belongs.

The architectural answer: when an interface migrates out of Domain into Application, the **implementation must also migrate out of Persistence into Application's per-feature `Infrastructure/` folder**, because Persistence cannot reference Application. This is feasible because the implementation only needs `ApplicationDbContext` and `BaseRepository<T,TKey>`, both reachable from Application (Application → Persistence is allowed).

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain
└── Features/Invoices/
    ├── IssuedInvoice.cs                       (unchanged — entity)
    ├── IssuedInvoiceSyncStats.cs              (stays — domain-meaningful stats)
    ├── IssuedInvoiceErrorType.cs              (unchanged)
    └── [IIssuedInvoiceRepository removed]     ← moved out
        [IssuedInvoiceFilters removed]         ← moved out
        [PaginatedResult<T> removed]           ← moved out

Anela.Heblo.Application
├── Shared/
│   └── PaginatedResult.cs                     ← NEW (generic, reusable)
└── Features/Invoices/
    ├── Contracts/
    │   ├── IssuedInvoiceFilters.cs            ← NEW (was in Domain)
    │   └── IIssuedInvoiceRepository.cs        ← NEW (was in Domain)
    └── Infrastructure/
        └── IssuedInvoiceRepository.cs         ← MOVED from Persistence

Anela.Heblo.Persistence
└── Features/Invoices/
    └── [IssuedInvoiceRepository.cs removed]   ← moved to Application
```

### Key Design Decisions

#### Decision 1: Choose Option A — move interface to Application — but also move the implementation

**Options considered:**
- **Option A (spec):** Move interface to Application. Implementation stays in Persistence. **Rejected — circular reference**: Persistence does not reference Application in this solution, so the implementation cannot see the interface.
- **Option A′ (recommended):** Move interface to Application AND move implementation to `Application/Features/Invoices/Infrastructure/`. Implementation continues to use `ApplicationDbContext` + `BaseRepository<T,TKey>` (both in Persistence; Application → Persistence is allowed).
- **Option B (spec):** Split the interface — Domain keeps a minimal domain-meaningful interface; Application introduces a pagination/sort query service that wraps it. **Rejected**: doubles the surface area for a structural refactor that the spec explicitly says must be behavior-preserving.
- **Option C:** Put `PaginatedResult<T>` in `Xcc` and `IssuedInvoiceFilters` in Domain `Contracts/`, keep interface in Domain. **Rejected**: `Xcc` is for technical cross-cutting concerns (HTTP, telemetry, generic repository abstraction). Pagination is an application concept; putting it in Xcc just relocates the smell. Also, `IssuedInvoiceFilters` is unambiguously application-vocabulary and does not belong in Domain.

**Chosen approach:** **Option A′.**

**Rationale:**
- Matches the codebase's existing inverted dependency direction (`Application → Persistence`).
- Consistent with existing precedent: `Application/Features/Purchase/ISupplierRepository.cs` and `Application/Features/FeatureFlags/Infrastructure/HebloFeatureProvider.cs`.
- DI registration already lives in `Application/Features/Invoices/InvoicesModule.cs` — no DI restructure needed.
- Implementation has zero behavior change; only file path and namespace move.
- Restores the principle from the brief: an application-pagination-shaped repository contract is an application contract.

#### Decision 2: `PaginatedResult<T>` lives in `Application/Shared`, not in `Application/Features/Invoices/Contracts`

**Rationale:** The type is generic and has no Invoices semantics. Future modules adopting pagination should not have to import `Anela.Heblo.Application.Features.Invoices.Contracts`. `Application/Shared/` already exists (`BaseResponse.cs`, `ErrorCodes.cs`, `ListResponse.cs`) and is the right home.

#### Decision 3: `IssuedInvoiceSyncStats` stays in Domain

**Rationale:** It is a domain-meaningful aggregate (no pagination/sort/UI vocabulary). Only the three relocated types satisfy the brief's criterion. Per NFR-3, the grep of Domain for `PageNumber`, `PageSize`, `SortBy`, `TotalPages`, `HasNextPage`, `HasPreviousPage`, `ShowOnlyUnsynced`, `ShowOnlyWithErrors` is what must come up empty — those tokens do not appear in `IssuedInvoiceSyncStats`.

#### Decision 4: Do **not** introduce a `global using` alias

**Rationale:** The pre-existing `global using IFeatureFlagOverrideRepository = ...` alias only exists because that interface stayed in Domain. Here we are moving the canonical type — there is no namespace ambiguity to alias around. A `global using` alias for a type whose only home is Application adds confusion, not clarity.

## Implementation Guidance

### Directory / Module Structure

Create:
- `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs` (namespace `Anela.Heblo.Application.Shared`)
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` (namespace `Anela.Heblo.Application.Features.Invoices.Contracts`)
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` (namespace `Anela.Heblo.Application.Features.Invoices.Contracts`)
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` (namespace `Anela.Heblo.Application.Features.Invoices.Infrastructure`)

Delete:
- The three classes from `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`. Keep `IssuedInvoiceSyncStats` in Domain — extract it into its own file `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs` so Domain stops having a file whose name doesn't match its primary type.
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` (after moving content to Application/Features/Invoices/Infrastructure/).

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs
namespace Anela.Heblo.Application.Features.Invoices.Contracts;

using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Xcc.Persistance;

public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    // method signatures unchanged, modulo namespaces on parameter/return types
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    // ... all other existing methods, unchanged
}
```

`IssuedInvoiceFilters` and `PaginatedResult<T>` keep **exactly** the public shape from `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs:101–128` — no property added, removed, renamed, retyped, or re-defaulted. DTOs stay `class` (project rule).

### Data Flow

Unchanged. Controller → MediatR request → `GetIssuedInvoicesListHandler` → `IIssuedInvoiceRepository.GetPaginatedAsync(filters)` → EF Core query → `PaginatedResult<IssuedInvoice>` → mapped to `IssuedInvoiceDto` → response. Only namespaces of intermediate types change.

### Call Sites to Update

Five files have hard references to one or more of the relocated types (`Grep` confirmed):

| File | Change |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` | Delete (replaced by Application copy). Move `IssuedInvoiceSyncStats` to its own file. |
| `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` | Delete (replaced by Application copy). |
| `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoicesList/GetIssuedInvoicesListHandler.cs` | Drop `using Anela.Heblo.Domain.Features.Invoices;` for the moved types (kept only for `IssuedInvoice`/`IssuedInvoiceSyncStats`). |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` | Update `using` to `Anela.Heblo.Application.Features.Invoices.Contracts` (filters) and `Anela.Heblo.Application.Shared` (paginated result). Likely also references the moved `IssuedInvoiceRepository` class — update to `Anela.Heblo.Application.Features.Invoices.Infrastructure`. |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoicesListHandlerPaginationTests.cs` | Same `using` updates. |

Also audit and update `using` directives in `Application/Features/Invoices/InvoicesModule.cs:2,4` — currently `using Anela.Heblo.Domain.Features.Invoices;` and `using Anela.Heblo.Persistence.Features.Invoices;` provide the `IIssuedInvoiceRepository` and `IssuedInvoiceRepository` references. After the move, both replace with `using Anela.Heblo.Application.Features.Invoices.Contracts;` and `using Anela.Heblo.Application.Features.Invoices.Infrastructure;`. The Domain `using` likely also stays for `IssuedInvoice` if referenced elsewhere in that file (only Bank's `IssuedInvoiceSyncStats` flow may keep it relevant).

### DI Registration

`Application/Features/Invoices/InvoicesModule.cs:20` already has:

```csharp
services.AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>();
```

The line is unchanged — only the `using` directives at the top swap. Check `Persistence/PersistenceModule.cs` for any duplicate registration of `IIssuedInvoiceRepository` or the concrete `IssuedInvoiceRepository`; remove if present (preliminary check did not show one in the visible head of that file, but the full file must be scanned).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer follows the spec's Option A literally and leaves the implementation in Persistence — solution fails to compile due to circular reference. | HIGH | Architecture review explicitly amends FR-3a (see **Specification Amendments**) — move both interface and implementation. |
| Test project `Anela.Heblo.Tests` may have additional `using` statements not surfaced by the symbol grep (e.g. fully-qualified usages, mock setups by string). | MEDIUM | Run full `dotnet build` after edits; treat warning CS0246 (type not found) as the first failure to fix; grep for `Domain.Features.Invoices` and `Persistence.Features.Invoices` across `backend/test/`. |
| `IssuedInvoiceSyncStats` reformat (split into its own file) breaks a `using static` or unusual reference. | LOW | Verified file is currently the home of both `IIssuedInvoiceRepository` and `IssuedInvoiceSyncStats`. Splitting preserves the namespace, so only file location changes. |
| EF Core may have cached migration metadata pointing to old assembly. | LOW | No `DbSet` configuration moves (entity config stays in Persistence). No migration needed. Project rule states migrations are manual anyway. |
| Other modules (e.g. Bank, MarketingInvoices) may import the relocated symbols transitively. | LOW–MED | The symbol grep returned exactly 5 files. Re-run `Grep "Anela\\.Heblo\\.Domain\\.Features\\.Invoices\\.(IssuedInvoiceFilters\|PaginatedResult)"` post-edit to confirm zero hits. |
| Frontend regeneration triggers due to namespace changes in handler response shapes. | NONE | Handler response shape (`GetIssuedInvoicesListResponse`) is unchanged. OpenAPI surface is byte-identical. |

## Specification Amendments

**Amend FR-3a.** The chosen resolution must be:

> **Option A′ (Architect-mandated):** Move `IIssuedInvoiceRepository` to `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`. **Additionally**, move the implementation from `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` to `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs`. The implementation continues to depend on `ApplicationDbContext` and `BaseRepository<T,TKey>` from `Anela.Heblo.Persistence` — this is permitted because `Application` already references `Persistence`. Persistence must **not** reference Application (would create a circular project reference). The premise in the original FR-3a that "Persistence already depends on Application" is incorrect for this solution.

**Amend FR-4.** Add a sixth file to the call-site list: `Application/Features/Invoices/InvoicesModule.cs` (its `using` block must be updated; the `AddScoped` line is unchanged).

**Amend FR-5.** Add: "`IssuedInvoiceSyncStats` is extracted into `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs` (same namespace, no shape change). This is a no-op refactor that does not require test changes beyond the implicit one-file rename."

**Add NFR-6 (architectural test).** After this change, the existing reflection test in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` should be extended (or a new assertion added) verifying that no type in `Anela.Heblo.Domain.*` references `Anela.Heblo.Application.*` and that the three relocated type names do not appear under `Anela.Heblo.Domain`. This is the cheapest possible guard against regression.

## Prerequisites

None. No migrations, no infrastructure, no configuration, no Key Vault changes, no new NuGet packages. The change is contained within the .NET solution and is safe to ship in a single PR.

Post-implementation gate (per project CLAUDE.md):

- `dotnet build` (zero new warnings)
- `dotnet format`
- `dotnet test` (all touched test projects pass without behavioral edits)
- No E2E impact — Playwright suite need not run for this change.
```