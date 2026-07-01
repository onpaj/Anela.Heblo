# Specification: Surface bank statement import outcome counts (success / error) end-to-end

## Summary
The `POST /api/bank-statements/import` endpoint returns only the list of imported statements and silently drops the per-run outcome counts, so the UI cannot tell a partially failed import from a fully successful one. This spec adds success/error counts to the import result contract, populates them from the handler (which already computes them but discards them), and updates the import success message in `ImportTab.tsx` to show the counts and flag any failures.

## Background
When an operator triggers a manual bank statement import, the handler `ImportBankStatementHandler` processes each statement returned by the bank client, records each as either successful (`ImportResult == "OK"`) or failed, and locally computes `successCount` / `errorCount` for logging. It then returns an `ImportBankStatementResponse` that carries only `Statements`.

The controller `BankStatementsController.ImportStatements` maps this to `BankStatementImportResultDto`, which also exposes only `Statements`. The frontend `ImportTab.tsx` ignores the response body entirely and shows a fixed alert `Import dokončen pro datum {importDate}` on any non-throwing response. As a result, a run where some statements failed (e.g. 3 OK, 1 error) is visually indistinguishable from a fully successful run; the operator must open the statement list, filter by errors, and inspect rows to discover a partial failure.

This is an architecture-review finding: the controller performs a lossy DTO transformation, and information the domain already produces is never surfaced to the user.

**Correction to the brief (grounded in the source):** The brief states that `ImportBankStatementResponse` already *carries* `SuccessCount`, `ErrorCount`, `SkippedCount`, and `HasErrors`. In the current code it does **not** — `ImportBankStatementResponse` exposes only `Statements` (plus the `BaseResponse` members `Success` / `ErrorCode` / `Params`). The handler computes `successCount` and `errorCount` as local variables (`ImportBankStatementHandler.cs:113-114`) purely for a log line and discards them at the `return`. There is currently **no "skipped" concept**: every statement from the bank client becomes either a success (`ImportResult == "OK"`) or an error; nothing is skipped. This spec therefore treats the counts as fields to be *added and populated* across the response, contract, and UI, and scopes `SkippedCount` / `HasErrors` per the decisions below rather than assuming they already exist.

## Functional Requirements

### FR-1: Import result contract exposes outcome counts
`BankStatementImportResultDto` (`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportResultDto.cs`) MUST expose the per-run outcome counts in addition to `Statements`:
- `SuccessCount` (int) — number of statements imported successfully (`ImportResult == ImportStatus.Success`, i.e. `"OK"`).
- `ErrorCount` (int) — number of statements that failed to import (total minus successful).
- `TotalCount` (int) — total number of statements processed in the run (equal to `Statements.Count`).
- `HasErrors` (bool) — `true` when `ErrorCount > 0`.

The DTO MUST remain a plain C# class with public get/set auto-properties (per the project rule that OpenAPI-facing DTOs are classes, never records). `HasErrors` MAY be a computed read-only property (`ErrorCount > 0`) since it is derived; if a computed property causes issues with the OpenAPI TypeScript generator, fall back to a settable property populated by the controller.

**Acceptance criteria:**
- `BankStatementImportResultDto` compiles with public `Statements`, `SuccessCount`, `ErrorCount`, `TotalCount`, and `HasErrors` members.
- The DTO is a class, not a record.
- Existing consumers of `Statements` continue to compile unchanged.

### FR-2: Handler returns outcome counts
`ImportBankStatementResponse` (`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs`) MUST carry `SuccessCount` and `ErrorCount` (and, for symmetry, expose `TotalCount` / `HasErrors` — either as fields or derived from `Statements` and `SuccessCount`). `ImportBankStatementHandler.Handle` MUST populate them from the values it already computes (`successCount`, `errorCount`) instead of discarding them.

The counting rule MUST be preserved exactly as today:
- Success = count of imports whose `ImportResult == ImportStatus.Success` (`"OK"`).
- Error = total imports minus success count (covers `PROCESSING_ERROR`, `UNKNOWN_ERROR`, and any service-provided error message).

**Acceptance criteria:**
- After a run producing N statements of which S are `"OK"`, the response has `SuccessCount == S`, `ErrorCount == N - S`, `TotalCount == N`.
- The existing structured log line (`Bank import COMPLETED ... Success ... Errors ...`) still reports the same numbers.

### FR-3: Controller maps counts without lossy transformation
`BankStatementsController.ImportStatements` (`backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs:52-55`) MUST populate `SuccessCount`, `ErrorCount`, `TotalCount`, and `HasErrors` on `BankStatementImportResultDto` from the handler response, alongside `Statements`. The controller MUST NOT drop any outcome field the handler now provides.

To honor the architecture guideline that lossy/business DTO shaping does not belong in the controller, the preferred approach is: have the handler return `BankStatementImportResultDto` directly (making `ImportBankStatementResponse` unnecessary), so the controller performs a straight pass-through with no field-by-field reconstruction. If keeping the separate response type is preferred for consistency with other use cases, the controller mapping MUST copy every outcome field (an AutoMapper profile or explicit assignment of all fields is acceptable). The chosen approach is recorded in Open Questions for the architect to confirm.

**Acceptance criteria:**
- The response body of `POST /api/bank-statements/import` includes `statements`, `successCount`, `errorCount`, `totalCount`, and `hasErrors`.
- No outcome field produced by the handler is omitted from the API response.
- Error paths (`ArgumentException` → 400, generic → 500) are unchanged.

### FR-4: Frontend surfaces the import outcome
`ImportTab.tsx` (`handleImportSubmit`, lines ~159-166) MUST read the returned counts and reflect them in the completion message instead of the fixed `Import dokončen pro datum {importDate}` alert.

- On a fully successful run (`hasErrors === false`): show a success message including the successful count, e.g. `Import dokončen pro datum {importDate}: {successCount} výpisů úspěšně naimportováno.` When `totalCount === 0`, show a "no statements found" variant, e.g. `Import dokončen pro datum {importDate}: žádné výpisy k importu.`
- On a partial-failure run (`hasErrors === true`): show a warning-style message that makes the failure explicit, e.g. `Import dokončen pro datum {importDate}: {successCount} úspěšně, {errorCount} s chybou. Zkontrolujte seznam výpisů.`

The hand-written response interfaces in `useBankStatements.ts` (`BankImportResponse`, and the unused `BankStatementImportResult`) MUST be extended with `successCount`, `errorCount`, `totalCount`, and `hasErrors` so the component is typed against the new fields. The existing `alert(...)` mechanism MAY be reused; no new toast/notification component is required.

**Acceptance criteria:**
- After a partial-failure import, the completion message states both counts and does not read as an unqualified success.
- After a fully successful import, the message reports the number imported.
- After an import that returns zero statements, the message indicates nothing was imported (no misleading count).
- The TypeScript build passes with the response types updated.

### FR-5: Regenerated OpenAPI client reflects the new contract
The C# `BankStatementImportResultDto` flows into the generated OpenAPI clients (C# and TypeScript) on build. After the DTO change, the generated clients MUST expose the new fields. Any generated-client artifacts that are committed MUST be regenerated so they are consistent with the contract.

**Acceptance criteria:**
- `dotnet build` regenerates without error and the generated TypeScript client (if consumed) exposes the new fields.
- No stale generated artifact contradicts the new DTO shape.

## Non-Functional Requirements

### NFR-1: Performance
No new I/O, queries, or bank-client calls are introduced. The counts are computed in-memory over the already-materialized `imports` list (O(n) over the statements in a single run, typically a handful). Endpoint latency MUST be unchanged within noise.

### NFR-2: Security
No change to the authorization surface. The endpoint remains gated by `[FeatureAuthorize(Feature.Customer_BankStatements)]`. The new fields are aggregate counts derived from data the caller is already authorized to see (they already receive the full `Statements` list); no additional data sensitivity is introduced.

### NFR-3: Backward compatibility
The change is additive: existing fields (`statements`) keep their name and shape. Adding fields to the response is non-breaking for existing consumers. No API version bump is required.

### NFR-4: Localization
User-facing strings are Czech, consistent with the rest of `ImportTab.tsx` (e.g. `Import dokončen`, `Zkontrolujte seznam výpisů`). Wording is illustrative in this spec; the implementer SHOULD match existing tone and phrasing in the file.

## Data Model
No persistence or schema changes. The affected types are in-memory DTOs/response objects only:

- `BankStatementImport` (domain entity) — unchanged. Each instance has `ImportResult` set to `ImportStatus.Success` (`"OK"`) on success, or an error string on failure. This is the source of truth for the counts.
- `ImportBankStatementResponse` — gains `SuccessCount`, `ErrorCount` (and derived `TotalCount` / `HasErrors`), or is removed in favor of returning `BankStatementImportResultDto` directly (see FR-3).
- `BankStatementImportResultDto` — gains `SuccessCount`, `ErrorCount`, `TotalCount`, `HasErrors` alongside existing `Statements`.
- Frontend `BankImportResponse` interface — gains the matching camelCase fields.

Counting relationships:
- `TotalCount == Statements.Count`
- `ErrorCount == TotalCount - SuccessCount`
- `HasErrors == ErrorCount > 0`
- `SuccessCount == Statements.count(s => s.ImportResult == "OK")`

## API / Interface Design

### `POST /api/bank-statements/import`
Request (unchanged) — `BankImportRequestDto`: `{ accountName, dateFrom, dateTo }`.

Response body (extended), JSON:
```json
{
  "statements": [ /* BankStatementImportDto[] — unchanged */ ],
  "successCount": 3,
  "errorCount": 1,
  "totalCount": 4,
  "hasErrors": true
}
```
Status codes unchanged: 200 on success, 400 on `ArgumentException` (e.g. unknown account), 500 on unexpected error.

### Frontend flow (`ImportTab.tsx`)
1. Operator opens the import modal, picks an account and date, clicks "Spustit import".
2. `handleImportSubmit` calls `importMutation.mutateAsync(...)` and awaits the response.
3. The handler inspects `hasErrors` / `successCount` / `errorCount` / `totalCount` and renders the appropriate Czech message (success / partial-failure / nothing-imported) via the existing `alert(...)`.
4. The statement list is refetched and the modal closes, as today.

## Dependencies
- MediatR request/response pipeline for the import use case (existing).
- AutoMapper — only if the controller keeps a separate response type and maps it (existing dependency; a mapping profile or explicit assignment).
- OpenAPI client generation on build (C# + TypeScript) — see `docs/development/api-client-generation.md`.
- No external service, no database migration, no new library.

## Out of Scope
- Persisting import-run summaries or history (the counts are returned per request only).
- Any change to how statements are fetched, parsed, or imported (bank client, `IBankStatementImportService`).
- Replacing `alert(...)` with a richer toast/notification/UI component, or adding a results panel/table to the modal.
- Adding an "errors only" auto-filter or auto-navigation after a partial-failure import.
- Introducing a genuine "skipped" state in the import pipeline (there is no skip logic today). `SkippedCount` from the brief is intentionally not implemented as a real counter — see Open Questions.
- Changes to the list endpoint (`GET /api/bank-statements`) or its DTOs.

## Open Questions
1. **`SkippedCount`:** The current handler has no skip concept — every statement is a success or an error, so `SkippedCount` would always be 0. Confirm the intended handling: (a) omit `SkippedCount` entirely (recommended — do not add a field that is always 0), or (b) include `SkippedCount` as a constant 0 for forward-compatibility / to match the brief's field list. This spec assumes (a).
2. **Controller vs. handler shaping:** The architecture finding prefers moving DTO shaping out of the controller. Confirm the preferred approach for FR-3: (a) handler returns `BankStatementImportResultDto` directly and `ImportBankStatementResponse` is removed (cleanest per the finding), or (b) keep `ImportBankStatementResponse` and map all fields in the controller. This spec recommends (a).

## Status: HAS_QUESTIONS
