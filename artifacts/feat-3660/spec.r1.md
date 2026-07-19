# Specification: Move `IMarketingTransactionSource` and `MarketingTransaction` from Domain to Application/Contracts

## Summary
`IMarketingTransactionSource` and `MarketingTransaction` currently live in the Domain layer (`Anela.Heblo.Domain.Features.MarketingInvoices`) even though they are an adapter port and its DTO, not domain concepts. This change relocates both types to `Anela.Heblo.Application/Features/MarketingInvoices/Contracts/`, updates all namespace/using references across the adapters, Application services, and tests, and removes the now-unnecessary Domain dependency for these two types. This is a pure structural/namespace refactor with no behavioral change.

## Background
Per `docs/architecture/development_guidelines.md`, the Domain layer should contain only entities, value objects, domain service interfaces, and repository contracts. Per `docs/architecture/filesystem.md`, feature-level contracts consumed by Application services belong in `Application/Features/{Feature}/Contracts/`.

`IMarketingTransactionSource` is an outbound port abstracting HTTP/SDK calls to third-party ad platforms (Meta, Google), and `MarketingTransaction` is the raw-payload-carrying DTO it returns. Neither has domain invariants or behavior. They were placed in Domain only as a lowest-common-denominator location visible to both the adapter projects and the Application layer, avoiding a circular reference. Moving both types together to Application removes that constraint, since the adapter projects (`Anela.Heblo.Adapters.MetaAds`, `Anela.Heblo.Adapters.GoogleAds`) already reference `Anela.Heblo.Application`.

This was identified by the daily arch-review routine (issue #3660) as a Clean Architecture layering violation in the MarketingInvoices module.

## Functional Requirements

### FR-1: Relocate `IMarketingTransactionSource`
Move the interface from `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IMarketingTransactionSource.cs` to `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs`, changing its namespace from `Anela.Heblo.Domain.Features.MarketingInvoices` to `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`. The interface body (members, signatures, XML docs if any) is otherwise unchanged.

**Acceptance criteria:**
- The file no longer exists under `Anela.Heblo.Domain`.
- The file exists at `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/IMarketingTransactionSource.cs` with namespace `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`.
- The interface's member signatures (`Platform`, `GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct)`) are byte-for-byte identical to before, aside from the namespace line.

### FR-2: Relocate `MarketingTransaction`
Move the class from `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs` to `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs`, changing its namespace from `Anela.Heblo.Domain.Features.MarketingInvoices` to `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`. All properties (`TransactionId`, `Amount`, `TransactionDate`, `Description`, `Currency`, `RawData`) remain unchanged.

**Acceptance criteria:**
- The file no longer exists under `Anela.Heblo.Domain`.
- The file exists at `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Contracts/MarketingTransaction.cs` with namespace `Anela.Heblo.Application.Features.MarketingInvoices.Contracts`.
- `MarketingTransaction` remains a plain C# class (not a record), consistent with the project's DTO convention.

### FR-3: Update all references to the moved types
Update `namespace`/`using` declarations in every file that references either type so the solution builds cleanly with the new locations. Based on the current codebase, the following files reference these types and must be updated:

- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/IMarketingInvoiceImportService.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

A repo-wide search for `IMarketingTransactionSource` and `MarketingTransaction` (excluding `ImportedMarketingTransaction`, which is a distinct, correctly-placed Domain entity and must not be touched) must be performed as part of implementation to catch any reference not enumerated above.

**Acceptance criteria:**
- No file outside `Anela.Heblo.Application` contains a `using Anela.Heblo.Domain.Features.MarketingInvoices;` that was only needed for these two types (remove the using if it becomes unused; keep it if `ImportedMarketingTransaction`/`IImportedMarketingTransactionRepository` from the same namespace are still used in that file).
- All files listed above compile against the new `Anela.Heblo.Application.Features.MarketingInvoices.Contracts` namespace.
- `git grep -n "Domain.Features.MarketingInvoices.*MarketingTransaction\b"` (or equivalent) returns no hits referencing the moved types.

### FR-4: Verify and, if needed, update project references
Confirm whether `Anela.Heblo.Adapters.MetaAds.csproj` and `Anela.Heblo.Adapters.GoogleAds.csproj` need any changes. As of this spec, both already carry a `<ProjectReference Include="..\..\Anela.Heblo.Application\Anela.Heblo.Application.csproj" />` (added previously for `ImportMarketingInvoicesRequest`/`ImportMarketingInvoicesResponse`), so no `.csproj` edits are expected to be necessary for this move. Confirm during implementation that no adapter project still requires a reference to `Anela.Heblo.Domain` solely for these two types, and remove such a reference only if it is otherwise unused by that project.

**Acceptance criteria:**
- `Anela.Heblo.Adapters.MetaAds.csproj` and `Anela.Heblo.Adapters.GoogleAds.csproj` reference `Anela.Heblo.Application` (already true; verify, don't assume).
- Neither adapter project's compile step depends on a `Anela.Heblo.Domain` reference for `IMarketingTransactionSource`/`MarketingTransaction` (either that reference is removed if now dead, or it is retained because still needed for something else — implementer confirms which, and does not remove a still-needed reference).

### FR-5: No behavioral change
This is strictly a code-organization refactor. No public API contracts, MediatR request/response shapes, HTTP endpoints, database schema, or runtime behavior may change.

**Acceptance criteria:**
- No changes to any controller, MediatR request/response class, or DTO exposed to the frontend.
- No changes to `ImportedMarketingTransaction` or `IImportedMarketingTransactionRepository` (these remain correctly in Domain and are out of scope).
- Existing unit tests in `ImportMarketingInvoicesHandlerTests.cs` and `MarketingInvoiceImportServiceTests.cs` pass unmodified in behavior (only namespace/using updates permitted).
- The generated OpenAPI/TypeScript client is unaffected (these types are not exposed through the API surface).

## Non-Functional Requirements

### NFR-1: Performance
N/A — no runtime code paths change; this is a compile-time namespace relocation.

### NFR-2: Security
N/A — no change to authentication, authorization, secrets handling, or data exposure. `RawData` (raw third-party JSON) continues to flow through the same code paths as before.

## Data Model
No persisted data model changes. `ImportedMarketingTransaction` (the EF-mapped entity) and its repository interface are untouched and remain in `Anela.Heblo.Domain.Features.MarketingInvoices`. `MarketingTransaction` is an in-memory transient DTO (never persisted) whose shape is unchanged — only its namespace/assembly location moves.

## API / Interface Design
No public HTTP API, MediatR contract, or frontend-facing interface changes. The only "interface" affected is the internal C# port `IMarketingTransactionSource`, whose member signatures are preserved exactly; only its namespace and assembly (Domain → Application) change.

## Dependencies
- `Anela.Heblo.Adapters.MetaAds` and `Anela.Heblo.Adapters.GoogleAds` projects (implement `IMarketingTransactionSource`).
- `Anela.Heblo.Application` project (new home for both types; must build with the new files added under `Features/MarketingInvoices/Contracts/`).
- `Anela.Heblo.Tests` project (unit tests referencing these types).
- Depends on the existing `ProjectReference` from both adapter `.csproj` files to `Anela.Heblo.Application` (already present, per FR-4).

## Out of Scope
- Any change to `ImportedMarketingTransaction` (entity) or `IImportedMarketingTransactionRepository` (repository interface) — these correctly remain in Domain.
- Any change to the shape, semantics, or validation of `MarketingTransaction`'s properties.
- Any change to MediatR requests/responses (`ImportMarketingInvoicesRequest`/`ImportMarketingInvoicesResponse`) beyond incidental `using` updates if they happen to reference the moved types indirectly.
- Any broader architectural cleanup of the MarketingInvoices module beyond this specific relocation.
- Adding new tests; existing test coverage is preserved as-is (moved/updated only for compilation, not expanded).

## Open Questions
None.

## Status: COMPLETE
