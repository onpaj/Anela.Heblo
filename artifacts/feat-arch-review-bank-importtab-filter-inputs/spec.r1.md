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
Users can filter the list by a partial, case-insensitive match on the bank account string (account number / IBAN as stored on the statement).

**Acceptance criteria:**
- Typing a value into the Account input and clicking "Filtrovat" restricts results to statements whose `Account` (or equivalent persisted field) contains the entered substring (case-insensitive).
- Whitespace at the start/end of the input is trimmed before being sent to the API.
- Empty input does not constrain results on that field.

### FR-3: Statement date range filter
Users can constrain results to statements whose statement date falls within an inclusive `[from, to]` range. Either bound may be supplied independently (open-ended ranges are allowed).

**Acceptance criteria:**
- Selecting only "From" returns statements with `StatementDate >= from`.
- Selecting only "To" returns statements with `StatementDate <= to`.
- Selecting both returns statements with `from <= StatementDate <= to` (inclusive on both ends).
- Date values are interpreted in the application's existing local-time convention (consistent with how `StatementDate` is currently displayed in the list column).
- Clearing both date pickers removes the date constraint.
- If "From" is later than "To", the UI must surface a validation error and block the request (do not send an invalid range to the backend).

### FR-4: "Errors only" filter
A checkbox restricts the list to bank statements that have at least one import error (i.e., statements where processing produced one or more error records, or whose status indicates an error state — see Open Questions for exact definition).

**Acceptance criteria:**
- When checked and "Filtrovat" is clicked, only statements meeting the "has errors" predicate are returned.
- When unchecked, the result set is not constrained by error state.
- The predicate is applied server-side.

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
- A filtered query returning zero results shows the existing empty-state message (no new copy required, but the message should not falsely suggest "no statements imported" — it must remain accurate when filters are the cause of emptiness; if the current copy does not distinguish, leave as-is and log this as a separate UX item).
- API errors surface through the existing error-handling path.

## Non-Functional Requirements

### NFR-1: Performance
- Filtered list queries must return within the same response-time envelope as the current unfiltered list query for typical result sizes (target p95 ≤ 500 ms for result sets up to one page).
- The backend must apply filters at the database level (LINQ-to-EF translated to SQL `WHERE` clauses). No in-memory post-filtering of large result sets.
- Existing indexes on `BankStatement` should be assessed; if filter performance regresses meaningfully on the production dataset, add indexes for the most selective fields (likely `TransferId`, `StatementDate`). Index additions, if any, ship as a separate migration referenced from the implementation PR.

### NFR-2: Security
- This endpoint already requires the application's standard authenticated session; no change to the authz posture.
- All filter parameters are bound through the request DTO and used in parameterized EF queries. No raw SQL concatenation.
- Input length is bounded on the backend (Transfer ID and Account: max 100 chars each) to reject pathological inputs at the contract boundary.

### NFR-3: API compatibility
- New query parameters are optional. Existing callers that omit them continue to receive unfiltered results, so the change is backward compatible.
- The generated TypeScript client must be regenerated as part of the build (per `docs/development/api-client-generation.md`).

### NFR-4: Testability
- Backend: unit/integration tests cover each filter individually and at least one combined-filter case, plus the "no filters" baseline. Use the existing repository test patterns for `IBankStatementRepository`.
- Frontend: at minimum, a component-level test that asserts changing inputs + clicking "Filtrovat" calls the hook with the expected request payload (and that "Vyčistit" sends an empty payload).
- E2E: not required for this change (per project policy E2E suite is nightly and module-scoped); rely on BE + FE unit/integration coverage.

## Data Model

No schema changes are required. The filters read existing fields on the `BankStatement` entity (or its persisted projection):

- `TransferId` (string) — already present and indexed (verify).
- `Account` (string) — bank account / IBAN field associated with the statement.
- `StatementDate` (date/datetime) — already present.
- Error-state predicate — derived. The exact derivation depends on how import errors are persisted (status enum on the statement vs. count of related error records). See Open Questions.

If `Account` is not currently stored on `BankStatement` directly (e.g., it lives on a related `BankAccount` entity), the filter joins through the existing relationship; no new column is added.

## API / Interface Design

### Backend
Extend the existing `GET /api/bank-statements` endpoint and the underlying `GetBankStatementListRequest` (MediatR query) with five optional query-string parameters:

| Parameter | Type | Semantics |
| --- | --- | --- |
| `transferId` | `string?` | Case-insensitive `Contains` match on `BankStatement.TransferId`. |
| `account` | `string?` | Case-insensitive `Contains` match on the account/IBAN field. Trimmed server-side. |
| `dateFrom` | `DateOnly?` (or `DateTime?` matching existing convention) | Inclusive lower bound on `StatementDate`. |
| `dateTo` | `DateOnly?` (or `DateTime?` matching existing convention) | Inclusive upper bound on `StatementDate`. |
| `errorsOnly` | `bool?` | When `true`, restrict to statements with import errors. When `null`/`false`, no constraint. |

Validation:
- Reject requests with `dateFrom > dateTo` via 400 (the frontend also blocks this, but defence-in-depth).
- Reject `transferId`/`account` longer than 100 characters via 400.

Repository:
- Extend `IBankStatementRepository.GetFilteredAsync` (or the equivalent already used by the list query) to accept the new criteria and translate them into EF `Where` clauses.
- Continue to return the same `(items, totalCount)` shape; `TotalCount` reflects the filtered total.

### Frontend
- Add the five optional fields to `GetBankStatementListRequest` in `frontend/src/api/hooks/useBankStatements.ts` (mirroring the regenerated OpenAPI client types — DTOs are classes, not records, per project rules).
- Pass the active filter state into the `useBankStatementsList` call in `ImportTab.tsx`.
- Move the "apply filters" trigger from a plain `refetch()` to setting a "committed filters" state object that the hook depends on; `refetch()` continues to work for manual refresh of the currently committed filter set.
- Reset `pageNumber` to 1 inside `handleApplyFilters` and `handleClearFilters`.
- Add a client-side validation guard for `dateFrom > dateTo` that surfaces an inline error and prevents submission.

### UI
No new controls. Existing controls keep their current placement, labels, and Czech copy. The "Filtrovat" / "Vyčistit" buttons retain their current behaviour from the user's perspective — they finally do what they appear to do.

## Dependencies
- Existing `BankStatement` aggregate and `IBankStatementRepository`.
- MediatR query/handler for the list endpoint.
- OpenAPI client generation pipeline (per `docs/development/api-client-generation.md`).
- React Query (or the project's existing data-fetching layer) as already used by `useBankStatementsList`.

No new external libraries.

## Out of Scope
- Adding new filter dimensions beyond the four already present in the UI (e.g., filtering by import status, by user, by amount).
- Persisting filter selections across sessions or via URL query string.
- Saved filter presets.
- Server-side full-text search; matching stays as simple `Contains`.
- Bulk actions on filtered results.
- Performance work beyond confirming the filtered query stays within the current response-time envelope (index tuning is included only if a regression is observed).
- E2E test coverage for this change (relies on BE + FE unit/integration tests).

## Open Questions
- **"Errors only" predicate definition.** The exact data shape for "has errors" is not specified. Two plausible models:
  (a) `BankStatement` carries a status enum where one or more values represent an error state, or
  (b) errors are stored as related records and the predicate is "any related error exists."
  The implementer should inspect the existing `BankStatement` aggregate / import pipeline and pick the model that matches reality, documenting the choice in the PR. If neither model fits cleanly, escalate before implementing FR-4.
- **Account field location.** Confirm whether `Account` is a direct column on `BankStatement` or lives on a related entity. The filter implementation should follow whichever relationship already exists; no new persistence is introduced.
- **Date type convention.** Use whichever date type (`DateOnly` vs. `DateTime`) the existing `StatementDate` and surrounding queries use. Do not introduce a new convention.

## Status: HAS_QUESTIONS