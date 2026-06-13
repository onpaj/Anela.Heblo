# Specification: Relocate IssuedInvoiceRepository to Persistence Layer

## Summary
Move `IssuedInvoiceRepository` from the Application layer to the Persistence layer to align the Invoices module with Clean Architecture and the established codebase convention (all ~35 other repositories live in `Anela.Heblo.Persistence`). This is a pure relocation with namespace and DI-binding updates — no behavioral changes.

## Background
The repository pattern in this codebase places the **interface** in the Application layer (`Features/<Module>/Contracts/`) and the **implementation** in the Persistence layer (`Anela.Heblo.Persistence/<Module>/`). The `development_guidelines.md` document codifies this explicitly: *"A repository's implementation lives in `Anela.Heblo.Persistence`"*.

`IssuedInvoiceRepository` is the lone exception. It currently lives at `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` and imports `Anela.Heblo.Persistence` and `Anela.Heblo.Persistence.Repositories`. This reverses the intended dependency direction (Application should not depend on Persistence), makes the Application project untestable in isolation without EF Core, and breaks the pattern that the upcoming Phase 2 migration to per-module `DbContext`s will rely on.

The interface `IIssuedInvoiceRepository` is already correctly placed in `Application/Features/Invoices/Contracts/` and does not need to move.

## Functional Requirements

### FR-1: Move repository implementation file
Relocate the file `IssuedInvoiceRepository.cs` from the Application project's Invoices/Infrastructure folder to the Persistence project's Invoices folder, co-located with the existing EF configurations (`IssuedInvoiceConfiguration.cs`, `IssuedInvoiceSyncDataConfiguration.cs`).

**Acceptance criteria:**
- File exists at `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`.
- File no longer exists at `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs`.
- If the `Application/Features/Invoices/Infrastructure/` directory becomes empty as a result, it is removed.

### FR-2: Update namespace
Change the namespace of the moved file from `Anela.Heblo.Application.Features.Invoices.Infrastructure` to `Anela.Heblo.Persistence.Invoices` to match its new location and the namespace pattern used by sibling repositories in the Persistence project.

**Acceptance criteria:**
- The file declares `namespace Anela.Heblo.Persistence.Invoices;` (or block-style equivalent matching the file's existing convention).
- The class continues to extend `BaseRepository<IssuedInvoice, string>` with the existing `ApplicationDbContext` constructor parameter.
- The `using Anela.Heblo.Persistence;` and `using Anela.Heblo.Persistence.Repositories;` imports are removed (now redundant — the file is in that assembly).
- A `using Anela.Heblo.Application.Features.Invoices.Contracts;` (or equivalent) is added so the implementation can reference `IIssuedInvoiceRepository`.

### FR-3: Remove Application → Persistence project reference (if introduced solely for this repository)
If the `Anela.Heblo.Application.csproj` project file references `Anela.Heblo.Persistence` only because of `IssuedInvoiceRepository`, remove that project reference to restore the intended dependency direction (Persistence → Application).

**Acceptance criteria:**
- After the move, `Anela.Heblo.Application` does not reference `Anela.Heblo.Persistence` unless another Application-layer type still requires it.
- If other types still require the reference, document why in the PR description and leave the reference in place.
- The solution builds cleanly with `dotnet build`.

### FR-4: Update DI binding
Update `InvoicesModule.cs` so the DI container resolves `IIssuedInvoiceRepository` to the implementation in its new location.

**Acceptance criteria:**
- `InvoicesModule.cs` compiles and registers `IIssuedInvoiceRepository → IssuedInvoiceRepository` using the new namespace.
- The application starts successfully and resolves `IIssuedInvoiceRepository` at runtime.
- No other DI registrations are altered.

### FR-5: Preserve all repository behavior
The repository's public API, methods, query logic, and runtime behavior must remain identical. This is a refactoring task only.

**Acceptance criteria:**
- All existing tests that exercise `IssuedInvoiceRepository` (directly or through handlers that depend on `IIssuedInvoiceRepository`) pass without modification.
- No method signatures, return types, or query semantics change.
- `dotnet format` produces no diff after the move.

### FR-6: Update consumer imports
Any consumer (handler, service, test) that imports `Anela.Heblo.Application.Features.Invoices.Infrastructure` solely to access `IssuedInvoiceRepository` must have its `using` statement updated or removed. Consumers should depend on the interface `IIssuedInvoiceRepository` from `Anela.Heblo.Application.Features.Invoices.Contracts` — direct references to the concrete class outside of DI registration are a smell and should be flagged in the PR description if found.

**Acceptance criteria:**
- A repository-wide search for `Anela.Heblo.Application.Features.Invoices.Infrastructure` returns no results after the change.
- No consumer fails to compile due to a missing or stale `using`.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact expected. Compile-time impact (assembly graph) is improved: removing `Application → Persistence` dependency reduces unnecessary coupling.

### NFR-2: Security
No security impact. No authentication, authorization, data access, or input validation logic changes.

### NFR-3: Testability
After the move, `Anela.Heblo.Application` no longer pulls in EF Core / `ApplicationDbContext` through `IssuedInvoiceRepository`. The Application project should compile and unit-test without referencing the Persistence project (subject to FR-3).

### NFR-4: Architectural compliance
The change must bring the Invoices module into compliance with `docs/architecture/development_guidelines.md` and match the established pattern used by ~35 other repositories (e.g., `ArticleRepository`, `LeafletDocumentRepository`, `PurchaseOrderRepository`, `PackingMaterialRepository`).

## Data Model
No data model changes. `IssuedInvoice` entity, `IssuedInvoiceConfiguration`, and `IssuedInvoiceSyncDataConfiguration` are untouched. No EF migrations required.

## API / Interface Design
No public API changes. The HTTP API surface is unaffected. The `IIssuedInvoiceRepository` interface contract is unchanged.

**Files affected (expected):**
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` — **deleted**
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — **added** (moved content with updated namespace and usings)
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — **modified** (updated using statement for the concrete type)
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — **possibly modified** (remove `Anela.Heblo.Persistence` project reference if no longer needed)
- Any consumer file using the old namespace — **modified** (using statement update)

## Dependencies
- `Anela.Heblo.Persistence` project must reference `Anela.Heblo.Application` (it already does — this is how it sees `IIssuedInvoiceRepository` and `IssuedInvoice`).
- `BaseRepository<TEntity, TKey>` in `Anela.Heblo.Persistence.Repositories` is the existing base class and is unchanged.
- `ApplicationDbContext` is unchanged.

## Out of Scope
- Renaming, redesigning, or refactoring the `IssuedInvoiceRepository` methods or query logic.
- Changes to the `IIssuedInvoiceRepository` interface, its location, or its members.
- Changes to other repositories that already correctly live in `Anela.Heblo.Persistence`.
- The Phase 2 migration to per-module `DbContext`s (this change merely unblocks/eases it).
- Changes to `IssuedInvoice` entity, its EF configurations, or the database schema.
- Adding new tests beyond ensuring existing tests still pass. (If no tests currently cover the repository, that gap is pre-existing and not in scope here.)

## Open Questions
None.

## Status: COMPLETE