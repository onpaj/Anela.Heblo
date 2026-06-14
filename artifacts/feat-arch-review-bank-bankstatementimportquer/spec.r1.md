# Specification: Remove Unused `BankStatementImportQueryDto` Dead Code

## Summary
Delete the unused `BankStatementImportQueryDto` class from the Bank module's `Contracts/` directory. This DTO is never referenced in the codebase and has been superseded by `GetBankStatementListRequest`. Its continued presence creates confusion about which contract is authoritative for bank statement list queries.

## Background
The Bank module's `Contracts/` directory contains `BankStatementImportQueryDto.cs`, defining a query DTO with `Id`, `StatementDate`, `ImportDate`, `Skip`, `Take`, `OrderBy`, and `Ascending` properties. A project-wide search confirms the type is never instantiated, referenced, or imported outside its declaring file.

The active query contract for listing bank statements is `GetBankStatementListRequest` (in `UseCases/GetBankStatementList/`), which is the request type wired into the MediatR handler and exposed via the API. `GetBankStatementListRequest` carries a richer field set (`TransferId`, `Account`, `DateFrom`, `DateTo`, `ErrorsOnly`) that the dead DTO lacks. The orphaned DTO therefore presents an incomplete and misleading view of the real query shape, which could lure a future developer into using or extending the wrong type.

This finding originates from the daily architecture review routine on 2026-06-03.

## Functional Requirements

### FR-1: Delete the dead DTO file
Remove `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportQueryDto.cs` from the repository.

**Acceptance criteria:**
- The file no longer exists in the working tree.
- The file is removed via `git rm` (or equivalent) so the deletion is recorded in version control.
- No other source files reference the type `BankStatementImportQueryDto` (verifiable via repository-wide grep returning zero hits).

### FR-2: Preserve the active query contract
`GetBankStatementListRequest` and all related handlers, validators, controllers, and tests in `UseCases/GetBankStatementList/` must remain untouched.

**Acceptance criteria:**
- No files under `UseCases/GetBankStatementList/` are modified.
- Existing behavior of the bank statement list endpoint is unchanged.

### FR-3: Verify build and test integrity after removal
Confirm the solution builds and existing tests still pass after the file is removed.

**Acceptance criteria:**
- `dotnet build` succeeds at the solution root with zero new errors or warnings attributable to the change.
- `dotnet format` reports no required changes.
- All Bank-module unit tests pass (`dotnet test` filtered to Bank-related test projects, or a full backend test run).
- No OpenAPI client regeneration drift occurs (the deleted DTO was not exposed via any controller, so the generated TypeScript client should be unaffected).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. This is a compile-time-only change removing an unreferenced class.

### NFR-2: Security
No security implications. The removed DTO was not part of any auth, input validation, or data exposure surface.

### NFR-3: Maintainability
Removing the dead type reduces cognitive load for future contributors to the Bank module and eliminates the risk of someone extending or using the wrong contract.

### NFR-4: Backwards compatibility
None required. The type is not referenced internally, not exported via any public API, and not part of the OpenAPI-generated client surface.

## Data Model
No data model changes. No entities, database tables, or persisted shapes are affected.

## API / Interface Design
No API changes. No controllers, MediatR handlers, MVC routes, or OpenAPI operations are modified. The TypeScript client generated from OpenAPI should produce an identical artifact after this change.

## Dependencies
- None. The change is a single-file deletion within the `Anela.Heblo.Application` project.

## Out of Scope
- Modifying, renaming, or refactoring `GetBankStatementListRequest` or any handler in `UseCases/GetBankStatementList/`.
- Auditing other modules for similar dead `Contracts/` types (separate review routine output).
- Reconciling field differences between the deleted DTO and the active request (the deleted DTO is being removed wholesale, not merged).
- Adding new tests; the change is a deletion of unreferenced code and is covered by the existing build and test suite.
- Updating documentation — no docs reference `BankStatementImportQueryDto`.

## Open Questions
None.

## Status: COMPLETE