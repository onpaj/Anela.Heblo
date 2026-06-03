Final reviewer says **READY TO MERGE**. All issues flagged are pre-existing patterns not introduced by this feature. The implementation is complete and correct.

---

# Implementation: Wire Up Bank Import Tab Filter Inputs

## What was implemented

All four filter controls on the Bank Import tab (Transfer ID, Account, statement-date range, errors-only) are now wired end-to-end from the UI through the frontend hook → controller → MediatR handler → repository → EF Core `WHERE` clauses against the existing `BankStatements` table. Clicking "Filtrovat" now actually constrains the list; "Vymazat" returns to the unfiltered state. No schema changes.

## Files created/modified

**Backend — new:**
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankStatementListFilter.cs` — sealed domain record packaging all filter criteria
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryIntegrationTests.cs` — Testcontainers Postgres tests for ILike case-insensitive matching and `%`/`_` wildcard escaping
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs` — handler unit tests + validator tests

**Backend — modified:**
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — registers `GetBankStatementListRequestValidator` and `ValidationBehavior` pipeline
- `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — new signature with `BankStatementListFilter` + `CancellationToken`
- `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` — five new `WHERE` clauses, `EscapeLike` helper, `ILike` filters for TransferId/Account
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListRequest.cs` — five new optional properties
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs` — parses `DateFrom`/`DateTo`, trims strings, builds full filter
- `backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs` — length caps, parseable-date rules, `DateFrom <= DateTo` cross-field rule
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs` — five new `[FromQuery]` parameters, `ValidationException` → 400
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportRepositoryTests.cs` — updated all call sites + 5 new filter tests

**Frontend — new:**
- `frontend/src/components/customer/tabs/__tests__/ImportTab.test.tsx` — 6 component tests for committed-filter wiring and date guard

**Frontend — modified:**
- `frontend/src/api/hooks/useBankStatements.ts` — extended `GetBankStatementListRequest` interface + URLSearchParams serialization
- `frontend/src/components/customer/tabs/ImportTab.tsx` — replaced no-op `refetch()` pattern with `committedFilters` object, date range validation guard, honest `(filtrováno)` indicator

## Tests

- `BankStatementImportRepositoryTests.cs` — 18 unit tests (13 existing + 5 new for ErrorsOnly, DateFrom, DateTo)
- `BankStatementImportRepositoryIntegrationTests.cs` — 6 Testcontainers integration tests for ILike filters (requires Docker; marked `[Trait("Category", "Integration")]`)
- `GetBankStatementListHandlerTests.cs` — 3 handler unit tests + 7 validator unit tests
- `ImportTab.test.tsx` — 6 component tests

**Backend: 4584 non-integration tests pass. Frontend: 1983 tests pass.**

## How to verify

```bash
# Backend
dotnet test backend/Anela.Heblo.sln --filter "Category!=Integration"
# With Docker (integration tests):
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~BankStatementImportRepositoryIntegrationTests"

# Frontend
cd frontend
npm test -- --testPathPattern='ImportTab' --watchAll=false
npm run build
npm run lint
```

## Notes

- Testcontainers integration tests require Docker locally; they will run in CI. Tests are committed and tagged `[Trait("Category", "Integration")]` so they can be excluded in environments without Docker.
- Pre-existing issues flagged by the final reviewer (`console.error` and `alert()` in the import flow, `SortableHeader` defined inside render) are not introduced by this feature and should be tracked separately.
- The generated TypeScript client (`frontend/src/api/generated/api-client.ts`) regenerates automatically on the next backend build.

## PR Summary

Wires the four pre-existing filter controls on the Bank Import tab (Transfer ID, Account, statement-date range, errors-only) end-to-end so clicking "Filtrovat" actually constrains the bank-statement list server-side. Previously the "Filtrovat" button performed a no-op refetch with the filters silently discarded.

Backend: introduces a `BankStatementListFilter` domain record to keep the repository signature maintainable, adds five optional query parameters to `GET /api/bank-statements`, wires FluentValidation pipeline (length caps, parseable-date rules, `DateFrom <= DateTo`), and implements EF `WHERE` clauses with `EF.Functions.ILike` + `EscapeLike` for case-insensitive substring matching. Frontend: replaces the no-op `refetch()` pattern with a `committedFilters` state object that React Query keys on (so only "Filtrovat"/"Vymazat" trigger a network request), surfaces a `dateFrom > dateTo` inline validation guard, and fixes the `(filtrováno)` footer indicator to reflect all five filters.

### Changes
- `BankStatementListFilter.cs` (new) — domain record for all filter criteria
- `IBankStatementImportRepository.cs` — new `GetFilteredAsync(BankStatementListFilter, ...)` signature
- `BankStatementImportRepository.cs` — five new `WHERE` clauses with `EscapeLike` helper
- `GetBankStatementListRequest.cs` — five new optional properties
- `GetBankStatementListHandler.cs` — parses strings to `DateTime?`, trims inputs, builds full filter
- `GetBankStatementListRequestValidator.cs` — length, parseable-date, and date-range rules
- `BankModule.cs` — registers validator + `ValidationBehavior` pipeline
- `BankStatementsController.cs` — five new `[FromQuery]` params + `ValidationException` → 400
- `useBankStatements.ts` — extended request interface + serialization
- `ImportTab.tsx` — committed-filter state, date validation guard, honest filter indicator
- New test files: `BankStatementImportRepositoryIntegrationTests.cs`, `GetBankStatementListHandlerTests.cs`, `ImportTab.test.tsx`

## Status
DONE_WITH_CONCERNS

*Concerns: Testcontainers integration tests could not be executed locally (Docker unavailable) — they compile and will run in CI. Pre-existing issues in ImportTab.tsx (`console.error`, `alert()`, `SortableHeader` inside render) are not introduced by this feature.*