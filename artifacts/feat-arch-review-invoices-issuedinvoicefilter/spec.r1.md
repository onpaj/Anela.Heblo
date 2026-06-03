# Specification: Relocate `IssuedInvoiceFilters` and `PaginatedResult<T>` out of Domain

## Summary
Move two application-level types — `IssuedInvoiceFilters` and the generic `PaginatedResult<T>` — out of the Invoices Domain project and into the Application layer. The Domain layer must remain free of pagination, sorting, and UI-driven query concerns, restoring Clean Architecture boundaries.

## Background
The current `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` declares two types that violate Clean Architecture's dependency rule:

- `IssuedInvoiceFilters` mixes domain filter predicates with application/UI concerns: `PageNumber`, `PageSize`, `SortBy`, `SortDescending`, plus UI-driven flags (`ShowOnlyUnsynced`, `ShowOnlyWithErrors`).
- `PaginatedResult<T>` is a generic pagination envelope with computed view-helper properties (`TotalPages`, `HasNextPage`, `HasPreviousPage`).

Neither type has business meaning in the Invoices domain. They exist because callers (handlers, controllers, frontend) need to page and sort results — concerns owned by the Application layer.

Consequences of the current placement:
1. The Domain project accumulates application vocabulary, blurring its role.
2. Any other module wanting `PaginatedResult<T>` would have to depend on `Anela.Heblo.Domain.Features.Invoices` for a generic utility — a coupling that has no semantic justification.
3. Future module-isolated persistence (e.g. per-module DbContexts) is harder because query mechanics are entangled with the repository contract.

The fix is a structural relocation with no behavioral change. The repository contract `IIssuedInvoiceRepository` continues to accept a filter object and return a paginated result; only the namespace and project of those two types change.

## Functional Requirements

### FR-1: Relocate `IssuedInvoiceFilters` to the Application layer
Move the `IssuedInvoiceFilters` class from `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` to a new file `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs` under the namespace `Anela.Heblo.Application.Features.Invoices.Contracts`.

The type's shape (properties, defaults, nullability) must be preserved exactly. No properties are added, removed, renamed, or retyped.

**Acceptance criteria:**
- `IssuedInvoiceFilters` no longer exists in `Anela.Heblo.Domain`.
- The class lives at `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IssuedInvoiceFilters.cs`.
- Namespace is `Anela.Heblo.Application.Features.Invoices.Contracts`.
- All public properties (`PageNumber`, `PageSize`, `SortBy`, `SortDescending`, `ShowOnlyUnsynced`, `ShowOnlyWithErrors`, plus any other existing filter predicates) retain identical names, types, default values, and accessibility.
- The Domain project compiles without any reference to `IssuedInvoiceFilters`.

### FR-2: Relocate `PaginatedResult<T>` to a shared Application namespace
Move `PaginatedResult<T>` from the Domain project to `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs` under namespace `Anela.Heblo.Application.Shared`. The type is generic and should be reusable across Application-layer features, not tied to Invoices.

**Acceptance criteria:**
- `PaginatedResult<T>` no longer exists in `Anela.Heblo.Domain`.
- The class lives at `backend/src/Anela.Heblo.Application/Shared/PaginatedResult.cs`.
- Namespace is `Anela.Heblo.Application.Shared`.
- All public members (`Items`, `TotalCount`, `PageNumber`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`, plus constructors) retain identical names, types, and behavior.
- Computed properties (`TotalPages`, `HasNextPage`, `HasPreviousPage`) produce identical values for identical inputs.

### FR-3: Update `IIssuedInvoiceRepository` contract
The repository interface continues to live in the Domain project. Its method signatures must reference the relocated types via their new namespaces.

**Acceptance criteria:**
- `IIssuedInvoiceRepository` remains in `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`.
- Method signatures that previously referenced `IssuedInvoiceFilters` and `PaginatedResult<T>` continue to reference them, but via the new namespaces.
- The interface's method names, parameter names, parameter order, return types (modulo namespace), and async signatures are unchanged.
- The Domain project now has a project reference to (or otherwise can resolve) the relocated types. **Note:** Domain must not reference Application. See Open Questions / Assumptions on how to resolve this — the resolution is captured in FR-3a.

### FR-3a: Resolve the Domain → Application dependency direction
Because the Domain layer cannot depend on the Application layer, and `IIssuedInvoiceRepository` lives in Domain but must reference the relocated filter/result types, one of the following resolutions must be applied. The implementer should choose **Option A** as the default unless the architect overrides:

- **Option A (recommended):** Move `IIssuedInvoiceRepository` itself out of Domain into the Application layer (e.g. `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`). A repository whose contract is parameterized by application-level pagination/sorting types is an application contract, not a domain contract. The Infrastructure/persistence project that implements it already depends on Application.
- **Option B:** Keep `IIssuedInvoiceRepository` in Domain, but split it: a domain-level interface that takes only domain-meaningful filter predicates, plus an application-level repository or query service that adds pagination/sorting and produces `PaginatedResult<T>`. This is a larger refactor.

**Acceptance criteria:**
- The chosen option is implemented end-to-end. No file references a type from a layer it must not depend on.
- The Domain project does not reference `Anela.Heblo.Application.*`.
- The solution compiles cleanly.

### FR-4: Update all call sites
Every consumer of `IssuedInvoiceFilters`, `PaginatedResult<T>`, or (if Option A is chosen in FR-3a) `IIssuedInvoiceRepository` must be updated to import the new namespace.

**Acceptance criteria:**
- All `using` directives across the solution reference the new namespaces.
- All MediatR handlers, controllers, mappers, tests, and infrastructure classes that previously referenced these types compile.
- No file in any layer references `Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceFilters` or `Anela.Heblo.Domain.Features.Invoices.PaginatedResult<T>` (those symbols no longer exist).
- `dotnet build` succeeds for the entire solution with zero warnings introduced by this change.

### FR-5: Preserve runtime behavior
This is a pure structural refactor. No method body, query, sort behavior, filter semantics, or API contract may change.

**Acceptance criteria:**
- All existing unit and integration tests covering issued-invoice listing, pagination, sorting, and filtering continue to pass without modification beyond namespace `using` updates.
- API endpoints that return paginated issued invoices return byte-identical JSON for identical inputs (modulo serialization of unchanged shapes).
- No new public API endpoint, request/response DTO, or database query is introduced or modified.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. This change is namespace/project relocation only. CI build time is unchanged or marginally affected (one new file each in Application, fewer in Domain).

### NFR-2: Security
No security surface changes. Auth, authorization, data exposure, and input validation are untouched.

### NFR-3: Architectural integrity
After this change, the Domain project must contain only:
- Domain entities,
- Domain value objects,
- Domain-level interfaces whose method signatures are expressed entirely in domain terms (or in primitives).

Specifically, a quick `grep` of the Domain project for `PageNumber`, `PageSize`, `SortBy`, `TotalPages`, `HasNextPage`, `HasPreviousPage`, `ShowOnlyUnsynced`, `ShowOnlyWithErrors` should return zero matches.

### NFR-4: Testability
Existing tests must compile after `using` updates only. No test logic should need to change.

### NFR-5: Code style
Follows the project's standard conventions:
- File-scoped namespaces or block-scoped namespaces matching the surrounding file's style.
- XML doc comments, if present on the original types, are preserved verbatim.
- DTOs remain `class`, not `record` (project rule).

## Data Model
No data model changes. No entity is added, removed, renamed, or modified. No database migration is required.

## API / Interface Design
No external API change.

Internal C# API changes (namespace only):

| Type | Old location | New location |
|---|---|---|
| `IssuedInvoiceFilters` | `Anela.Heblo.Domain.Features.Invoices` | `Anela.Heblo.Application.Features.Invoices.Contracts` |
| `PaginatedResult<T>` | `Anela.Heblo.Domain.Features.Invoices` | `Anela.Heblo.Application.Shared` |
| `IIssuedInvoiceRepository` | `Anela.Heblo.Domain.Features.Invoices` | (Option A) `Anela.Heblo.Application.Features.Invoices.Contracts`; (Option B) unchanged |

Frontend (React + generated TypeScript client) is unaffected because the OpenAPI surface does not change.

## Dependencies
- `Anela.Heblo.Application` project (existing). May need a small adjustment to expose the `Shared` folder/namespace.
- `Anela.Heblo.Infrastructure` (or wherever `IIssuedInvoiceRepository` is implemented) — already references Application, so it can resolve the moved types after `using` updates.
- No new NuGet packages.
- No external services.

## Out of Scope
- Generalizing `PaginatedResult<T>` for other modules in this change. Other modules may adopt it later; that is a separate task.
- Replacing `IssuedInvoiceFilters` with a more sophisticated query object (e.g. specification pattern, expression-based filtering). This refactor preserves the existing shape.
- Reviewing or relocating other Domain types that may have similar boundary smells (other repositories, other filter classes). Each is a separate finding.
- Frontend changes — none required, the public API surface is unchanged.
- Database schema or migration changes.
- Adjusting MediatR handler signatures beyond namespace updates.

## Open Questions
None.

## Status: COMPLETE