# Architecture Review: Relocate IssuedInvoiceRepository to Persistence Layer

## Skip Design: true

## Architectural Fit Assessment

The spec correctly identifies the symptom (a repository implementation in the wrong layer) but **mis-diagnoses the cause and produces an incomplete remediation**. After exploring the codebase, the situation is materially different from what the spec describes:

**Hard facts verified in the codebase:**

1. **`Anela.Heblo.Persistence` does NOT reference `Anela.Heblo.Application`.** The `.csproj` only references `Domain` and `Xcc`. A repository-wide grep finds zero `using Anela.Heblo.Application…` statements anywhere under `Anela.Heblo.Persistence/`. The spec's claim under *Dependencies* — *"Anela.Heblo.Persistence project must reference Anela.Heblo.Application (it already does)"* — is factually incorrect.
2. **The established convention places repository interfaces in `Domain`, not in `Application`.** Verified examples:
   - `IPackingMaterialRepository` lives at `Domain/Features/PackingMaterials/`, implemented at `Persistence/PackingMaterials/PackingMaterialRepository.cs`.
   - `IArticleRepository` lives at `Domain/Features/Article/`, implemented at `Persistence/Features/Article/ArticleRepository.cs`.
3. **`IIssuedInvoiceRepository` is therefore *also* in the wrong place** (not "already correctly placed" as the spec asserts). It currently lives at `Application/Features/Invoices/Contracts/`.
4. **The interface references types from Application** that prevent moving the implementation without also moving them: `PaginatedResult<T>` (`Application/Shared/`), `IssuedInvoiceFilters` (`Application/Features/Invoices/Contracts/`). `IssuedInvoiceSyncStats` is already correctly in `Domain/Features/Invoices/`.
5. **ADR-004 (in `development_guidelines.md`) confirms** the implementation file lives under `Persistence/{Feature}/` while the **DI binding** belongs in the owning `{Feature}Module.cs`. Other implementations are `public` precisely so the Application-layer module can bind them — which only works if the Application-layer module sees the implementation type, which only works because `Application → Persistence` is the (intended) one-way reference. **FR-3 ("remove `Application → Persistence`") is in direct conflict with the codebase's own established pattern** and must be re-examined.

**The real architectural fix is broader than the spec admits:** moving only the implementation file (as the spec proposes) is impossible without either (a) introducing a forbidden `Persistence → Application` reference, or (b) also moving the interface (and its dependent contract types) into `Domain`.

## Proposed Architecture

### Component Overview

```
Target layout (matches existing convention for Article, PackingMaterials, etc.)

  Anela.Heblo.Domain
    Features/Invoices/
      IssuedInvoice.cs                       (already here, unchanged)
      IssuedInvoiceSyncStats.cs              (already here, unchanged)
      IIssuedInvoiceRepository.cs            ◀── MOVE here from Application/Contracts/
      IssuedInvoiceFilters.cs                ◀── MOVE here from Application/Contracts/

  Anela.Heblo.Xcc
    Persistance/
      IRepository.cs                         (already here)
      PaginatedResult.cs                     ◀── MOVE here from Application/Shared/

  Anela.Heblo.Persistence                    (references Domain + Xcc only)
    Invoices/
      IssuedInvoiceConfiguration.cs          (already here)
      IssuedInvoiceSyncDataConfiguration.cs  (already here)
      IssuedInvoiceRepository.cs             ◀── MOVE here from Application/Infrastructure/

  Anela.Heblo.Application                    (references Domain + Persistence + Xcc)
    Features/Invoices/
      InvoicesModule.cs                      (update using statements; binding unchanged)
      …handlers, services, adapters…         (update using statements for renamed types)
```

### Key Design Decisions

#### Decision 1: Where the interface and contract types should live
**Options considered:**
- **(A)** Keep interface in `Application`, add a `Persistence → Application` reference.
- **(B)** Keep interface in `Application`, move only the implementation, and accept that the implementation cannot live in `Persistence` (status quo — what the spec actually preserves once FR-3 is honestly evaluated).
- **(C)** Move `IIssuedInvoiceRepository` + `IssuedInvoiceFilters` to `Domain`, move `PaginatedResult<T>` to `Xcc`, then move the implementation to `Persistence`.

**Chosen approach:** **(C)**.

**Rationale:** Only (C) is consistent with the existing codebase (Article, PackingMaterials, Leaflet, Bank, etc. all follow this exact pattern). (A) introduces a new reverse-direction dependency and contradicts ADR-004's "implementation file lives under `Persistence/{Feature}/`" while interfaces live in Domain. (B) is the current broken state. (C) is the only one that actually achieves the spec's stated goal (NFR-4: "match the established pattern used by ~35 other repositories").

#### Decision 2: Whether `Application → Persistence` reference should be removed
**Options considered:**
- Remove (per spec FR-3).
- Keep.

**Chosen approach:** **Keep the reference.**

**Rationale:** Per ADR-004, the DI binding for each repository lives in `Application/Features/{Feature}/{Feature}Module.cs` and binds the interface to the concrete class **by name** (`AddScoped<IFooRepository, FooRepository>()`). This requires the Application-layer module project to see the concrete class — i.e., to reference `Anela.Heblo.Persistence`. **This is the intended dependency direction in this codebase's Clean Architecture variant.** Other modules (e.g. `PackingMaterialsModule.cs` binding `PackingMaterialRepository`) work the same way today. FR-3 should be dropped from the spec; the architectural smell the brief calls out ("Application depends on Persistence") is actually load-bearing for the module wiring convention.

#### Decision 3: `PaginatedResult<T>` placement
**Options considered:**
- Move to `Anela.Heblo.Xcc.Persistance` (alongside `IRepository<T,TKey>`).
- Move to `Anela.Heblo.Domain/Common/`.
- Duplicate / leave it in `Application/Shared/` and break the interface contract.

**Chosen approach:** Move to `Anela.Heblo.Xcc.Persistance`.

**Rationale:** It is generic pagination infrastructure, conceptually a peer of `IRepository<T,TKey>` (which is already in Xcc). Domain should not own pagination metadata; Xcc already hosts the shared persistence primitives. Existing `using Anela.Heblo.Application.Shared;` statements in consumer code must be updated — verify scope with a repo-wide grep before merging.

## Implementation Guidance

### Directory / Module Structure

| Type | From | To | New namespace |
|---|---|---|---|
| `IIssuedInvoiceRepository` | `Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` | `Domain/Features/Invoices/IIssuedInvoiceRepository.cs` | `Anela.Heblo.Domain.Features.Invoices` |
| `IssuedInvoiceFilters` | `Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` | `Domain/Features/Invoices/IssuedInvoiceFilters.cs` | `Anela.Heblo.Domain.Features.Invoices` |
| `PaginatedResult<T>` | `Application/Shared/PaginatedResult.cs` | `Xcc/Persistance/PaginatedResult.cs` | `Anela.Heblo.Xcc.Persistance` |
| `IssuedInvoiceRepository` | `Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` | `Persistence/Invoices/IssuedInvoiceRepository.cs` | `Anela.Heblo.Persistence.Invoices` |

`Application/Features/Invoices/Contracts/` should remain — it still hosts other DTOs (e.g. command/query contracts) but the repository interface no longer belongs there.

The `Infrastructure/` folder in Application stays (it still contains transformations, adapters, jobs). Do **not** delete that folder.

### Interfaces and Contracts

After the move, `IIssuedInvoiceRepository` has the following signature/usings (no API change, only namespaces shift):

```csharp
namespace Anela.Heblo.Domain.Features.Invoices;

using Anela.Heblo.Xcc.Persistance;   // IRepository<T,TKey>, PaginatedResult<T>

public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    // identical members, identical signatures
}
```

The implementation:

```csharp
namespace Anela.Heblo.Persistence.Invoices;

using Anela.Heblo.Domain.Features.Invoices;   // IIssuedInvoice*, IssuedInvoice*
using Anela.Heblo.Persistence.Repositories;   // BaseRepository
using Anela.Heblo.Xcc.Persistance;            // PaginatedResult<T>
// EF Core + Logging usings unchanged

public class IssuedInvoiceRepository : BaseRepository<IssuedInvoice, string>, IIssuedInvoiceRepository
{
    // identical body
}
```

`InvoicesModule.cs` binding line is unchanged (`AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>()`) — only its `using` directives change.

### Data Flow

Unchanged. No behavioral or runtime change. The compile-time graph improves: `Anela.Heblo.Application` no longer contains EF Core query code; the Application assembly's compilation now only depends on `Persistence` for the concrete types it binds in modules, not for the repository implementation itself.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Spec is silent on moving the interface + `IssuedInvoiceFilters` + `PaginatedResult<T>` — implementer follows spec literally and gets stuck at compile time. | **High** | Adopt the *Specification Amendments* below before implementation starts. |
| Moving `PaginatedResult<T>` to Xcc forces `using Anela.Heblo.Application.Shared;` updates across the entire repo. | Medium | Repo-wide grep before merging; the change is mechanical and CI build will surface omissions. Confirm `PaginatedResult` has no other coupling to Application before moving. |
| FR-3 (drop `Application → Persistence` reference) conflicts with ADR-004's module-wiring convention. Removing the reference will break every other `Module.cs` that binds a Persistence repository. | **Critical** | Drop FR-3. Document the rationale (ADR-004) in the PR description. |
| Tests (`IssuedInvoiceRepositoryTests.cs`, `InvoiceImportServiceTests.cs`, etc.) use `using Anela.Heblo.Application.Features.Invoices.Infrastructure;`. | Low | Mechanical `using` update; all signatures preserved per FR-5. Verify all 6 affected test files. |
| Domain layer gains a `PaginatedResult<T>` dependency on Xcc — already true (Domain references Xcc via `IRepository`), so no new project-reference change required. | None | None. |
| Reviewers may push back on enlarging the scope vs. the spec ("just move one file"). | Medium | The scope expansion is **forced by architecture**, not optional. Without it, the move is impossible without breaking other rules. Make this clear in the PR description. |

## Specification Amendments

The following amendments to `spec.r1.md` are required before implementation:

1. **Amend "Dependencies" section.** Replace the false statement *"Anela.Heblo.Persistence project must reference Anela.Heblo.Application (it already does)"* with the verified fact: Persistence references **Domain** and **Xcc** only.

2. **Delete FR-3** ("Remove Application → Persistence project reference"). This reference is **required** by ADR-004 for module-wiring and is the established pattern across all other modules. The brief's framing of "Application depends on Persistence" as the bug is incorrect — that direction is intentional in this codebase.

3. **Add FR-7: Move `IIssuedInvoiceRepository` to Domain.**
   - From: `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`
   - To: `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`
   - New namespace: `Anela.Heblo.Domain.Features.Invoices`
   - Rationale: align with verified convention (`IPackingMaterialRepository`, `IArticleRepository`, etc., all in Domain).

4. **Add FR-8: Move `IssuedInvoiceFilters` to Domain.**
   - From: `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs`
   - To: `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceFilters.cs`
   - New namespace: `Anela.Heblo.Domain.Features.Invoices`
   - Rationale: it is a parameter type of the relocated interface; must travel with it.

5. **Add FR-9: Move `PaginatedResult<T>` to Xcc.**
   - From: `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs`
   - To: `backend/src/Anela.Heblo.Xcc/Persistance/PaginatedResult.cs`
   - New namespace: `Anela.Heblo.Xcc.Persistance`
   - Rationale: it is the return type of the relocated interface and is generic pagination infrastructure that already conceptually belongs alongside `IRepository<T,TKey>` in Xcc.
   - **Cross-cutting impact:** every consumer of `Anela.Heblo.Application.Shared.PaginatedResult` must update its `using` statement. Implementer must run a repo-wide search and fix all references in a single PR.

6. **Amend FR-2.** The new namespace and the implementation's `using` directives must include `Anela.Heblo.Domain.Features.Invoices` (for the interface + filters + sync stats) and `Anela.Heblo.Xcc.Persistance` (for `PaginatedResult<T>`). Drop the spec's suggestion to add `using Anela.Heblo.Application.Features.Invoices.Contracts;` — that namespace no longer hosts the interface.

7. **Amend FR-6.** Consumers will need two waves of `using` updates: one for the (now defunct) `Anela.Heblo.Application.Features.Invoices.Infrastructure` namespace, and one for the relocated contracts (`Anela.Heblo.Application.Features.Invoices.Contracts` → `Anela.Heblo.Domain.Features.Invoices` for interface + filters; `Anela.Heblo.Application.Shared` → `Anela.Heblo.Xcc.Persistance` for `PaginatedResult`).

8. **Amend "Files affected" list** to include the four moved files (interface, filters, paginated result, implementation) plus `Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` deletion and `Application/Shared/PaginatedResult.cs` deletion.

9. **Amend FR-5 acceptance criteria.** "All existing tests pass without modification" is too strong: test files using affected `using` statements **will** need their imports updated (mechanical, no logic changes). Reword to: *"No test method body changes; only `using` directives may be touched."*

## Prerequisites

- None at infrastructure / data / config level. No migrations, no Azure changes, no Key Vault changes.
- Confirm there are no in-flight PRs touching `IssuedInvoiceRepository`, `IIssuedInvoiceRepository`, `IssuedInvoiceFilters`, or `PaginatedResult<T>` before starting — the rename/move will conflict with any concurrent edit.
- Run `grep -rn "Anela.Heblo.Application.Shared" backend/` once before starting to scope the `PaginatedResult` `using`-update fan-out; commit a list of affected files to the PR description.