# Specification: Wire Up Bank Import Tab Filter Inputs

## Summary
The Bank module's Import tab (`ImportTab.tsx`) presents four filter controls — Transfer ID, Account, statement date range, and an "errors only" toggle — that currently have no effect on the displayed data. This specification wires those filters end-to-end (UI → frontend hook → API contract → backend repository) so clicking "Filtrovat" actually constrains the bank-statement list.

## Background
`frontend/src/components/customer/tabs/ImportTab.tsx` renders filter inputs and tracks their values in local state, but the `useBankStatementsList` hook is invoked with only pagination and sorting arguments. The filter state never reaches the API call, the frontend `GetBankStatementListRequest` interface lacks the corresponding properties, and the backend endpoint `GET /api/bank-statements` does not accept them. The "Filtrovat" button performs a no-op refetch.

Two remediation paths were proposed in the arch-review finding: (a) delete the dead filter UI, or (b) wire the filters through. The product decision is to **wire the filters through**, because:
- The filter set (transfer ID, account, date range, error flag) reflects real operational needs when reconciling imported bank statements.
- The UI was already designed and built for these filters; users expect them to work.
- Removing them would degrade the operational utility of the Import tab.

## Functional Requirements

### FR-1: Transfer ID filter
Users can filter the bank-statement list by a partial, case-insensitive match on Transfer ID (the unique import-batch identifier shown in the list column).

**Acceptance criteria:**
- Typing a value into the Transfer ID input and clicking "Filtrovat" restricts results to bank statements whose `TransferId` contains the entered substring (case-insensitive).
- An empty Transfer ID input does not constrain results on that field.
- The filter is applied server-side; the client does not post-filter results.
- Pagination respects the filtered result set (`TotalCount` reflects the filtered total).

### FR-2: Account filter
Users can filter the list by a partial, case-insensitive match on the `BankStatementImport.Account` column (the configured account *name*, e.g. `"ShoptetPay-CZK"` — not an IBAN). This matches the "Účet" column shown in the Import-tab grid.

**Acceptance criteria:**
- Typing a value into the Account input and clicking "Filtrovat" restricts results to statements whose `Account` column contains the entered substring (case-insensitive).
- Whitespace at the start/end of the input is trimmed before being sent to the API.
- Empty input does not constrain results on that field.
- The implementation is a single `Where` clause against the existing `Account` column — no join. On PostgreSQL use `EF.Functions.ILike(bs.Account, $"%{trimmedAccount}%")`. The existing `IX_BankStatements_Account` index is reused as-is.

### FR-3: Statement date range filter
Users can constrain results to statements whose `StatementDate` falls within an inclusive `[from, to]` range. Either bound may be supplied independently (open-ended ranges are allowed).

**Acceptance criteria:**
- Selecting only "From" returns statements with `StatementDate.Date >= dateFrom.Value.Date`.
- Selecting only "To" returns statements with `StatementDate.Date <= dateTo.Value.Date`.
- Selecting both returns statements with `dateFrom.Date <= StatementDate.Date <= dateTo.Date` (inclusive on both ends, day-granularity).
- `dateFrom` and `dateTo` travel on the wire as ISO date strings (`string?` query parameters), parsed in the handler via `DateTime.TryParse` into `DateTime?` — mirroring the existing `statementDate` / `importDate` convention in `GetBankStatementListHandler`. `DateOnly` is **not** introduced into the Bank module.
- Clearing both date pickers removes the date constraint.
- If "From" is later than "To", the UI surfaces an inline validation error and blocks the request; the backend also rejects such requests with 400 (defence-in-depth).

### FR-4: "Errors only" filter
A checkbox restricts the list to bank statements that did not import successfully. The predicate is `ImportResult != "OK"` (where `"OK"` is `ImportStatus.Success`).

**Acceptance criteria:**
- When checked and "Filtrovat" is clicked, only statements with `ImportResult != "OK"` are returned. This reuses the exact predicate that already drives `BankStatementImportDto.ErrorType` projection in `BankMappingProfile.cs` and the success-badge rendering in `ImportTab.tsx:205`.
- When unchecked, the result set is not constrained by error state.
- Implementation is a direct EF `Where(bs => bs.ImportResult != "OK")` clause against the existing `ImportResult` column on `BankStatementImport`. No new entity, column, or related-table predicate is introduced.

### FR-5: Combined filter behaviour
All four filters combine with AND semantics. Any subset of filters may be active simultaneously.

**Acceptance criteria:**
- Submitting multiple non-empty filters returns only statements that match every active criterion.
- Pagination, sorting, and filtering are mutually compatible — changing sort order or page does not reset filter values, and applying a filter resets pagination to page 1.

### FR-6: Filter application UX
The existing "Filtrovat" (apply) and "Vyčistit" (clear) buttons drive filter application explicitly; filters are not applied on every keystroke.

**Acceptance criteria:**
- Editing an input updates local state but does not trigger a network request.
- Clicking "Filtrovat" sends a single request with the current filter values and resets pagination to page 1.
- Clicking "Vyčistit" clears all filter inputs (transfer ID, account, both dates, errors-only) and immediately re-fetches the unfiltered list at page 1.
- The pre-existing pagination/sort behaviour for the list is preserved.

### FR-7: Empty / loading / error states
The list area continues to use the existing loading, empty, and error UI patterns established for the Import tab.

**Acceptance criteria:**
- While a filtered request is in flight, the existing loading indicator is shown.
- A filtered query returning zero results shows the existing empty-state message. If the current copy reads as "no statements imported" and would be misleading when filters are the cause of emptiness, leave the copy as-is for this change and log a separate UX item.
- API errors surface through the existing error-handling path.

## Non-Functional Requirements

### NFR-1: Performance
- Filtered list queries must return within the same response-time envelope as the current unfiltered list query for typical result sizes (target p95 ≤ 500 ms for result sets up to one page).
- The backend must apply filters at the database level (LINQ-to-EF translated to SQL `WHERE` clauses). No in-memory post-filtering of large result sets.
- Existing indexes are reused: `IX_BankStatements_Account` already covers the Account filter. If the `TransferId` / `StatementDate` filters regress meaningfully on the production dataset, index additions ship as a separate migration referenced from the implementation PR.

### NFR-2: Security
- This endpoint already requires the application's standard authenticated session; no change to the authz posture.
- All filter parameters are bound through the request DTO and used in parameterized EF queries. No raw SQL concatenation.
- Input length is bounded on the backend (Transfer ID and Account: max 100 chars each) to reject pathological inputs at the contract boundary.

### NFR-3: API compatibility
- New query parameters are optional. Existing callers that omit them continue to receive unfiltered results, so the change is backward compatible.
- The generated TypeScript client must be regenerated as part of the build (per `docs/development/api-client-generation.md`).

### NFR-4: Testability
- Backend: unit/integration tests cover each filter individually and at least one combined-filter case, plus the "no filters" baseline. Use the existing repository test patterns for the bank-statement list query.
- Frontend: at minimum, a component-level test that asserts changing inputs + clicking "Filtrovat" calls the hook with the expected request payload (and that "Vyčistit" sends an empty payload).
- E2E: not required for this change (per project policy E2E suite is nightly and module-scoped); rely on BE + FE unit/integration coverage.

## Data Model

No schema changes. All four filters read existing columns on `BankStatementImport` (table `BankStatements`):

| Filter | Column | Type | Notes |
| --- | --- | --- | --- |
| Transfer ID | `TransferId` | `string` | Existing column. |
| Account | `Account` | `string` (`text`) | Existing column, indexed by `IX_BankStatements_Account`. Stores the configured account *name* (e.g. `"ShoptetPay-CZK"`), not an IBAN. |
| Date range | `StatementDate` | `DateTime` (`timestamp without time zone`) | Existing column. Compared at day granularity. |
| Errors only | `ImportResult` | `string` | Existing column. `"OK"` on success, otherwise an error code/message. |

No new entities, columns, joins, or migrations are introduced.

## API / Interface Design

### Backend
Extend the existing `GET /api/bank-statements` endpoint and the underlying `GetBankStatementListRequest` (MediatR query) with five optional query-string parameters:

| Parameter | Wire type | Handler type | Semantics |
| --- | --- | --- | --- |
| `transferId` | `string?` | `string?` | Case-insensitive `Contains` match on `BankStatementImport.TransferId`. |
| `account` | `string?` | `string?` | Case-insensitive `Contains` match on `BankStatementImport.Account`, trimmed server-side. PostgreSQL implementation uses `EF.Functions.ILike`. |
| `dateFrom` | `string?` (ISO date) | `DateTime?` (parsed via `DateTime.TryParse`) | Inclusive lower bound on `StatementDate.Date`. |
| `dateTo` | `string?` (ISO date) | `DateTime?` (parsed via `DateTime.TryParse`) | Inclusive upper bound on `StatementDate.Date`. |
| `errorsOnly` | `bool?` | `bool?` | When `true`, applies `Where(bs => bs.ImportResult != "OK")`. When `null` / `false`, no constraint. |

Wire-shape choice follows the existing handler convention (`statementDate`, `importDate` are already `string?` parsed via `DateTime.TryParse`). DTOs remain classes, not records, per project rules.

Validation:
- Reject requests with `dateFrom > dateTo` via 400.
- Reject `transferId` / `account` longer than 100 characters via 400.
- Reject unparseable date strings via 400 (consistent with existing behaviour, or by extending it if currently lenient).

Repository:
- Extend the repository method used by the list query (`GetBankStatementListHandler` → `IBankStatementRepository`) to accept the new criteria and translate them into EF `Where` clauses.
- Continue to return the same `(items, totalCount)` shape; `TotalCount` reflects the filtered total.
- Use `EF.Functions.ILike` for `transferId` and `account` substring matches on PostgreSQL.
- Use `bs.StatementDate.Date >= dateFrom.Value.Date` / `<= dateTo.Value.Date` for the date range.

### Frontend
- Add the five optional fields to `GetBankStatementListRequest` in `frontend/src/api/hooks/useBankStatements.ts`, mirroring the regenerated OpenAPI client types. The DTO stays a class per project rules. Date fields are sent as ISO date strings.
- Pass the active filter state into the `useBankStatementsList` call in `ImportTab.tsx`.
- Move the "apply filters" trigger from a plain `refetch()` to setting a "committed filters" state object that the hook depends on; `refetch()` continues to work for manual refresh of the currently committed filter set.
- Reset `pageNumber` to 1 inside `handleApplyFilters` and `handleClearFilters`.
- Trim string filters (`transferId`, `account`) before sending; omit empty strings (send `undefined`).
- Add a client-side validation guard for `dateFrom > dateTo` that surfaces an inline error and prevents submission.

### UI
No new controls. Existing controls keep their current placement, labels, and Czech copy. The "Filtrovat" / "Vyčistit" buttons retain their current behaviour from the user's perspective — they finally do what they appear to do.

## Dependencies
- Existing `BankStatementImport` aggregate, `IBankStatementRepository`, and its EF configuration (`BankStatementImportConfiguration`).
- Existing `GetBankStatementListHandler` (MediatR) and the `GET /api/bank-statements` controller action.
- `BankMappingProfile` (no changes required; consumed for context on the `ErrorType` projection).
- OpenAPI client generation pipeline (per `docs/development/api-client-generation.md`).
- React Query (or the project's existing data-fetching layer) as already used by `useBankStatementsList`.
- PostgreSQL `ILIKE` operator via `EF.Functions.ILike` (already available; no new package).

No new external libraries.

## Out of Scope
- Adding new filter dimensions beyond the four already present in the UI (e.g., filtering by user, by amount, by specific error code).
- Persisting filter selections across sessions or via URL query string.
- Saved filter presets.
- Server-side full-text search; matching stays as simple `Contains` / `ILike`.
- Bulk actions on filtered results.
- Schema changes, new indexes (unless a measured regression demands one), or refactoring the `ImportResult` string column into a typed enum.
- Performance work beyond confirming the filtered query stays within the current response-time envelope.
- E2E test coverage for this change (relies on BE + FE unit/integration tests).
- Improvements to the empty-state copy when emptiness is filter-induced (tracked separately if surfaced).

## Open Questions
None.

## Status: COMPLETE