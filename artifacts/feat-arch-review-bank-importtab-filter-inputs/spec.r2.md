# Specification: Wire Up Bank Import Tab Filter Inputs

## Summary
The Bank module's Import tab (`ImportTab.tsx`) presents four filter controls — Transfer ID, Account, statement date range, and an "errors only" toggle — that currently have no effect on the displayed data. This specification wires those filters end-to-end (UI → frontend hook → API contract → backend repository) so clicking "Filtrovat" actually constrains the bank-statement list.

## Background
`frontend/src/components/customer/tabs/ImportTab.tsx` renders filter inputs and tracks their values in local state, but the `useBankStatementsList` hook is invoked with only pagination and sorting arguments. The filter state never reaches the API call, the frontend `GetBankStatementListRequest` interface lacks the corresponding properties, and the backend endpoint `GET /api/bank-statements` does not accept them. The "Filtrovat" button performs a no-op refetch.

Two remediation paths were proposed in the arch-review finding: (a) delete the dead filter UI, or (b) wire the filters through. The product decision is to **wire the filters through**, because:
- The filter set (transfer ID, account, date range, error flag) reflects real operational needs when reconciling imported bank statements.
- The UI was already designed and built for these filters; users expect them to work.
- Removing them would degrade the operational utility of the Import tab.

The persistence aggregate involved is `BankStatementImport` (table `BankStatements`), which already carries every column needed by the four filters; no schema changes are introduced.

## Functional Requirements

### FR-1: Transfer ID filter
Users can filter the bank-statement list by a partial, case-insensitive match on Transfer ID (the unique import-batch identifier shown in the list column).

**Acceptance criteria:**
- Typing a value into the Transfer ID input and clicking "Filtrovat" restricts results to bank statements whose `TransferId` contains the entered substring (case-insensitive).
- An empty Transfer ID input does not constrain results on that field.
- The filter is applied server-side; the client does not post-filter results.
- Pagination respects the filtered result set (`TotalCount` reflects the filtered total).

### FR-2: Account filter
Users can filter the list by a partial, case-insensitive match on the bank account string (the configured account name as stored on `BankStatementImport.Account`, e.g. `"ShoptetPay-CZK"` — this is the same value the user sees in the "Účet" column).

**Acceptance criteria:**
- Typing a value into the Account input and clicking "Filtrovat" restricts results to statements whose `BankStatementImport.Account` contains the entered substring (case-insensitive).
- Whitespace at the start/end of the input is trimmed before being sent to the API.
- The match is implemented as a single `ILike` clause against the `Account` column on PostgreSQL (`EF.Functions.ILike(bs.Account, $"%{trimmedAccount}%")`), with no join.
- Empty input does not constrain results on that field.

### FR-3: Statement date range filter
Users can constrain results to statements whose statement date falls within an inclusive `[from, to]` range. Either bound may be supplied independently (open-ended ranges are allowed).

**Acceptance criteria:**
- Selecting only "From" returns statements with `StatementDate.Date >= dateFrom.Date`.
- Selecting only "To" returns statements with `StatementDate.Date <= dateTo.Date`.
- Selecting both returns statements with `dateFrom.Date <= StatementDate.Date <= dateTo.Date` (inclusive on both ends, day granularity).
- Bounds are compared against the date portion of `StatementDate` (which is stored as `timestamp without time zone`) so that time-of-day on the persisted value never excludes a statement from a matching day.
- Clearing both date pickers removes the date constraint.
- If "From" is later than "To", the UI must surface an inline validation error and block the request (do not send an invalid range to the backend). The backend additionally rejects `dateFrom > dateTo` as defence-in-depth (HTTP 400).

### FR-4: "Errors only" filter
A checkbox restricts the list to bank statements whose `ImportResult` indicates a failed import.

**Acceptance criteria:**
- The "has errors" predicate is exactly `BankStatementImport.ImportResult != "OK"`, applied as an EF `Where` clause against the `ImportResult` column. This matches the predicate already used by the success-badge logic in `ImportTab.tsx` and by the existing `ErrorType` projection in `BankMappingProfile`.
- When the checkbox is checked and "Filtrovat" is clicked, only statements where `ImportResult != "OK"` are returned.
- When the checkbox is unchecked, the result set is not constrained by `ImportResult`.
- The predicate is applied server-side.
- No new column, related entity, or status enum is introduced; the implementation reads the existing `ImportResult` string column.

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
- A filtered query returning zero results shows the existing empty-state message. If the current copy does not distinguish "no statements imported" from "no statements match the filter", leave the copy as-is and log this as a separate UX item — copy changes are out of scope for this feature.
- API errors surface through the existing error-handling path.

## Non-Functional Requirements

### NFR-1: Performance
- Filtered list queries must return within the same response-time envelope as the current unfiltered list query for typical result sizes (target p95 ≤ 500 ms for result sets up to one page).
- The backend must apply filters at the database level (LINQ-to-EF translated to SQL `WHERE` clauses). No in-memory post-filtering of large result sets.
- The existing index `IX_BankStatements_Account` already covers the `Account` filter. `TransferId` and `StatementDate` should be assessed against the production dataset; if filter performance regresses meaningfully, add indexes as a separate migration referenced from the implementation PR. No proactive index work is required by this spec.

### NFR-2: Security
- This endpoint already requires the application's standard authenticated session; no change to the authz posture.
- All filter parameters are bound through the request DTO and used in parameterized EF queries (including `EF.Functions.ILike` for `Account` and `Contains` for `TransferId`). No raw SQL concatenation.
- Input length is bounded on the backend (Transfer ID and Account: max 100 chars each) to reject pathological inputs at the contract boundary (HTTP 400).

### NFR-3: API compatibility
- New query parameters are optional. Existing callers that omit them continue to receive unfiltered results, so the change is backward compatible.
- The generated TypeScript client must be regenerated as part of the build (per `docs/development/api-client-generation.md`).
- New DTO fields are declared as classes (not C# records), per project rule for OpenAPI-generated contracts.

### NFR-4: Testability
- Backend: unit/integration tests cover each filter individually and at least one combined-filter case, plus the "no filters" baseline. Use the existing repository test patterns for `BankStatementImportRepository`. Include explicit coverage of the `ImportResult != "OK"` predicate (both `"OK"` and non-OK values such as `PROCESSING_ERROR: …` and `UNKNOWN_ERROR`).
- Backend: a handler-level test asserts that the `dateFrom > dateTo` validation returns HTTP 400 and never reaches the repository.
- Frontend: at minimum, a component-level test that asserts changing inputs + clicking "Filtrovat" calls the hook with the expected request payload (transferId, account, dateFrom, dateTo, errorsOnly), and that "Vyčistit" sends an empty payload and resets pagination to page 1.
- E2E: not required for this change (per project policy E2E suite is nightly and module-scoped); rely on BE + FE unit/integration coverage.

## Data Model

No schema changes are required. The filters read existing fields on the `BankStatementImport` aggregate (table `BankStatements`):

- `TransferId` (`string`) — already present on `BankStatementImport`.
- `Account` (`string`, required) — direct column on `BankStatementImport`, mapped to `text`, already covered by the existing non-unique index `IX_BankStatements_Account`. Stores the configured account name (e.g. `"ShoptetPay-CZK"`), not an IBAN.
- `StatementDate` (`DateTime`) — direct column on `BankStatementImport`, mapped to `timestamp without time zone`.
- `ImportResult` (`string`) — direct column on `BankStatementImport`. Value is `"OK"` on success; otherwise a backend-supplied error message (e.g. `PROCESSING_ERROR: …`, `UNKNOWN_ERROR`). The "errors only" predicate is `ImportResult != "OK"`.

No new columns, related entities, or status enums are introduced.

## API / Interface Design

### Backend
Extend the existing `GET /api/bank-statements` endpoint and the underlying `GetBankStatementListRequest` (MediatR query) with five optional query-string parameters:

| Parameter | Wire type (controller / OpenAPI) | In-handler type | Semantics |
| --- | --- | --- | --- |
| `transferId` | `string?` | `string?` | Case-insensitive `Contains` match on `BankStatementImport.TransferId`. Max 100 chars. |
| `account` | `string?` | `string?` (trimmed) | Case-insensitive `Contains` match implemented as `EF.Functions.ILike` on `BankStatementImport.Account`. Trimmed server-side. Max 100 chars. |
| `dateFrom` | `string?` (ISO date) | `DateTime?` (parsed via `DateTime.TryParse`) | Inclusive lower bound on `StatementDate` (day granularity, compared via `.Date`). |
| `dateTo` | `string?` (ISO date) | `DateTime?` (parsed via `DateTime.TryParse`) | Inclusive upper bound on `StatementDate` (day granularity, compared via `.Date`). |
| `errorsOnly` | `bool?` | `bool?` | When `true`, restrict to statements with `ImportResult != "OK"`. When `null`/`false`, no constraint. |

The string-on-the-wire / `DateTime?`-in-handler convention mirrors the existing `statementDate` / `importDate` parameters already accepted by `GetBankStatementListHandler` and parsed via `DateTime.TryParse`. No `DateOnly` type is introduced in the Bank module.

Validation:
- Reject requests with `dateFrom > dateTo` via HTTP 400 (defence-in-depth; the frontend also blocks this).
- Reject `transferId` / `account` longer than 100 characters via HTTP 400.
- Reject unparseable `dateFrom` / `dateTo` strings via HTTP 400, matching how the existing date parameters behave.

Repository:
- Extend the existing list query in `BankStatementImportRepository` to accept the new criteria and translate each into an EF `Where` clause directly on the `BankStatementImport` queryable.
- Continue to return the same `(items, totalCount)` shape; `TotalCount` reflects the filtered total.

### Frontend
- Add the five optional fields (`transferId`, `account`, `dateFrom`, `dateTo`, `errorsOnly`) to `GetBankStatementListRequest` in `frontend/src/api/hooks/useBankStatements.ts`, mirroring the regenerated OpenAPI client types. DTOs are classes, not records, per project rules.
- ISO date strings are sent as-is for `dateFrom` / `dateTo`, matching the existing convention; the `<input type="date">` controls in `ImportTab.tsx` already produce this format.
- Pass the active filter state into the `useBankStatementsList` call in `ImportTab.tsx`.
- Move the "apply filters" trigger from a plain `refetch()` to setting a "committed filters" state object that the hook depends on; `refetch()` continues to work for manual refresh of the currently committed filter set.
- Reset `pageNumber` to 1 inside `handleApplyFilters` and `handleClearFilters`.
- Add a client-side validation guard for `dateFrom > dateTo` that surfaces an inline error and prevents submission.
- Use the absolute-URL pattern (`${apiClient.baseUrl}${relativeUrl}`) for any direct fetch — the request still goes through the existing generated hook, so no manual fetch should be needed.

### UI
No new controls. Existing controls keep their current placement, labels, and Czech copy. The "Filtrovat" / "Vyčistit" buttons retain their current behaviour from the user's perspective — they finally do what they appear to do.

## Dependencies
- Existing `BankStatementImport` aggregate and `BankStatementImportRepository`.
- Existing MediatR query/handler for the list endpoint (`GetBankStatementListHandler`).
- Existing AutoMapper `BankMappingProfile` (no changes required — the `ImportResult` / `ErrorType` mapping is already consistent with the FR-4 predicate).
- OpenAPI client generation pipeline (per `docs/development/api-client-generation.md`).
- React Query (or the project's existing data-fetching layer) as already used by `useBankStatementsList`.

No new external libraries.

## Out of Scope
- Adding new filter dimensions beyond the four already present in the UI (e.g., filtering by user, by amount, by specific error subtype).
- Persisting filter selections across sessions or via URL query string.
- Saved filter presets.
- Server-side full-text search; matching stays as simple `Contains` / `ILike`.
- Bulk actions on filtered results.
- Performance work beyond confirming the filtered query stays within the current response-time envelope (index tuning on `TransferId` / `StatementDate` is included only if a regression is observed).
- Refining the empty-state copy to distinguish "no imports yet" from "no matches for current filter".
- E2E test coverage for this change (relies on BE + FE unit/integration tests).
- Introducing `DateOnly` anywhere in the Bank module.

## Open Questions
None.

## Status: COMPLETE