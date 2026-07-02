# Architecture Review: Surface bank statement import outcome counts (success / error) end-to-end

## Skip Design: true

This is a bug fix that surfaces already-computed aggregate counts through an existing DTO and reuses the existing `alert(...)` message in `ImportTab.tsx`. No new UI components, screens, layouts, or visual design decisions are introduced. The only user-facing change is the text of an alert string (Czech copy already present in the file's tone). No design work.

## Architectural Fit Assessment

The change fits cleanly into existing patterns. It touches exactly one vertical slice (`Bank`) across the standard three points:

- **Contract** — `BankStatementImportResultDto` in `Application/Features/Bank/Contracts/` (the correct, module-owned location per the DTO ownership rule; the API project only consumes it).
- **Handler / Response** — `ImportBankStatementResponse` + `ImportBankStatementHandler` in `Application/Features/Bank/UseCases/ImportBankStatement/`.
- **Controller** — `BankStatementsController.ImportStatements` in `API/Controllers/`, a thin MediatR orchestrator.
- **Frontend** — the hand-written hook `useBankStatements.ts` and the consumer `ImportTab.tsx`.

The finding is real and correct: the handler already computes `successCount` / `errorCount` (`ImportBankStatementHandler.cs:113-114`) purely for a log line (line 116-118) and discards them at the `return` (line 120). The controller then does a lossy single-field projection (`BankStatementsController.cs:52-55`). Nothing about the fix requires new abstractions — it is additive plumbing over data the domain already produces.

**Correction to the brief, confirmed against source:** `ImportBankStatementResponse` does **not** currently carry `SuccessCount`/`ErrorCount`/`SkippedCount`/`HasErrors` — it exposes only `Statements` (plus `BaseResponse` members). There is no "skipped" concept in the pipeline: every statement from the bank client becomes either `ImportResult == ImportStatus.Success` (`"OK"`) or an error string (`PROCESSING_ERROR: ...`, `UNKNOWN_ERROR`, or a service error message). The spec's read is correct and the brief's is not.

**Skipped decision:** I confirm the spec's Open Question 1 recommendation — **omit `SkippedCount` entirely.** Adding a field that is provably always `0` is dead API surface. If a genuine skip state is introduced later, it is a non-breaking additive change at that time.

## Proposed Architecture

### Component Overview

```
ImportTab.tsx ──mutateAsync──▶ useBankStatementImport (useBankStatements.ts)
   │                                    │  POST /api/bank-statements/import
   │◀── BankImportResponse ─────────────┘
   │      { statements, successCount, errorCount, totalCount, hasErrors }
   ▼
 alert(...) branches on hasErrors / totalCount

        ┌─────────────────────── backend ───────────────────────┐
        │  BankStatementsController.ImportStatements             │
        │      └─ _mediator.Send(ImportBankStatementRequest)     │
        │            └─ ImportBankStatementHandler.Handle        │
        │                 • builds imports: List<...Dto>         │
        │                 • successCount = count(OK)             │
        │                 • errorCount   = total - success       │
        │                 • returns ImportBankStatementResponse  │
        │                     { Statements, SuccessCount,        │
        │                       ErrorCount }  (+ derived)        │
        │      └─ pass-through → BankStatementImportResultDto     │
        └────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Keep `ImportBankStatementResponse`; do NOT collapse it into the DTO
**Options considered:**
- (a) Handler returns `BankStatementImportResultDto` directly; delete `ImportBankStatementResponse` (spec's recommended Open Question 2 option).
- (b) Keep `ImportBankStatementResponse : BaseResponse`; controller maps all fields to `BankStatementImportResultDto`.

**Chosen approach:** (b) — keep the separate response type.

**Rationale:** Every other Bank use case (`GetBankStatementList`, `GetBankAccounts`, `GetBankStatementImportStatistics`) returns a `{UseCase}Response : BaseResponse`. `ImportBankStatementResponse` inherits `BaseResponse`, giving handlers the standard `Success`/`ErrorCode`/`Params` envelope used across the codebase. Deleting it to have the handler return a `Contracts/` DTO directly would make this one use case an inconsistent outlier and drop the `BaseResponse` envelope. The finding's real complaint is **lossiness**, not the existence of a response type — a response type that maps *all* fields is not lossy. This is the smaller, lower-risk change and matches the CLAUDE.md instruction to avoid new abstractions/inconsistency for a small bug fix. The controller stays a thin orchestrator that copies every field.

#### Decision 2: `HasErrors` and `TotalCount` are computed read-only properties
**Options considered:** settable auto-properties populated by the controller vs. derived read-only expression-bodied properties.

**Chosen approach:** Make `TotalCount => Statements.Count` and `HasErrors => ErrorCount > 0` computed read-only properties on the DTO; only `SuccessCount` and `ErrorCount` are settable auto-properties that the controller populates.

**Rationale:** These two are pure functions of the other fields — a settable pair invites drift (a caller could set `HasErrors = false` with `ErrorCount = 3`). NSwag serializes read-only getters into the response JSON and generates the matching TypeScript fields, so the OpenAPI contract is unaffected. **Mitigation baked in:** if the NSwag generator mis-handles a computed property on this DTO (rare but the project rule about records exists precisely because generators are finicky), fall back to plain settable properties set in the controller. Verify the generated TypeScript client after `dotnet build`.

#### Decision 3: Frontend consumes the hand-written hook types, not the generated client
**Rationale:** `useBankStatementImport` in `useBankStatements.ts` is a hand-rolled `fetch` mutation returning a hand-written `BankImportResponse` interface (it does not use the generated client). The fix therefore only requires extending that interface (and the unused `BankStatementImportResult`) with the four camelCase fields. The generated OpenAPI TypeScript client still regenerates on build (FR-5) and must not contradict the DTO, but `ImportTab.tsx` reads the hand-written type.

## Implementation Guidance

### Directory / Module Structure
No new files. Edit in place:

- `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportResultDto.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`
- `frontend/src/api/hooks/useBankStatements.ts`
- `frontend/src/components/customer/tabs/ImportTab.tsx`
- Regenerated: `frontend/src/api/generated/api-client.ts` and `backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs` (via build, do not hand-edit).

### Interfaces and Contracts

`BankStatementImportResultDto` (remains a plain C# class, never a record):
```csharp
public class BankStatementImportResultDto
{
    public List<BankStatementImportDto> Statements { get; set; } = new();
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int TotalCount => Statements.Count;   // computed
    public bool HasErrors => ErrorCount > 0;      // computed
}
```

`ImportBankStatementResponse` gains the same `SuccessCount` / `ErrorCount` settable properties (and may expose the same two computed members). The handler sets them from its existing locals; do **not** recompute or change the counting rule:
```csharp
return new ImportBankStatementResponse
{
    Statements = imports,
    SuccessCount = successCount,   // imports.Count(i => i.ImportResult == ImportStatus.Success)
    ErrorCount = errorCount,       // imports.Count - successCount
};
```

Controller — straight field copy, no logic:
```csharp
var result = new BankStatementImportResultDto
{
    Statements = response.Statements,
    SuccessCount = response.SuccessCount,
    ErrorCount = response.ErrorCount,
};
```
(`TotalCount` / `HasErrors` derive themselves.) Error paths (`ArgumentException` → 400, generic → 500) stay untouched.

Frontend interface (`useBankStatements.ts`):
```ts
export interface BankImportResponse {
  statements: BankStatementImportDto[];
  successCount: number;
  errorCount: number;
  totalCount: number;
  hasErrors: boolean;
}
```
Extend `BankStatementImportResult` identically (it is currently unused but kept in sync per FR-4).

### Data Flow
1. Operator submits import; `ImportBankStatementHandler` processes each statement, marking `ImportResult` as `"OK"` or an error string, and accumulates them into `imports`.
2. Handler computes `successCount` (already at line 113) and `errorCount` (line 114) and now assigns them onto the response instead of only logging them.
3. Controller copies `Statements`, `SuccessCount`, `ErrorCount` onto `BankStatementImportResultDto`; `TotalCount` and `HasErrors` derive; returns `200 Ok`.
4. `useBankStatementImport` returns the JSON body typed as `BankImportResponse`.
5. `handleImportSubmit` branches:
   - `totalCount === 0` → "nothing to import" message.
   - `hasErrors === true` → warning-style message stating both counts.
   - else → success message with `successCount`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| NSwag mis-serializes computed read-only DTO properties, breaking the generated TS/C# client | Low | Verify generated `api-client.ts` after `dotnet build`; if fields are missing, fall back to settable auto-properties populated in the controller (Decision 2 fallback). |
| Counting rule accidentally changed (e.g. counting `PROCESSING_ERROR` as success) | Low | Reuse the exact existing locals (`successCount`/`errorCount`); do not re-derive. Assert the log line still reports identical numbers (FR-2 acceptance). |
| Hand-written `BankImportResponse` drifts from the generated client | Low | Keep both in sync per FR-4/FR-5; the component reads the hand-written type but the generated client must not contradict the DTO. |
| `alert()`-based UX remains crude for partial failures | Low (accepted) | Out of scope by spec; richer notification is explicitly deferred. |

## Specification Amendments
- **Resolve Open Question 1 → option (a):** omit `SkippedCount` entirely. It would always be `0`; do not add dead fields.
- **Resolve Open Question 2 → option (b):** keep `ImportBankStatementResponse` and copy all fields in the controller (see Decision 1). This overrides the spec's tentative recommendation of (a). Rationale: consistency with every other Bank use case and retention of the `BaseResponse` envelope; the finding's lossiness concern is fully addressed by copying all fields.
- **`TotalCount` / `HasErrors` are derived, not stored** (Decision 2). The spec's FR-1 already allows `HasErrors` to be computed; extend the same treatment to `TotalCount`. FR-3's acceptance criterion ("controller populates `TotalCount`/`HasErrors`") is satisfied by derivation — the controller need not assign them explicitly.

## Prerequisites
None. No migration, no config, no infrastructure, no new library. AutoMapper is already a handler dependency but is **not** needed for the controller mapping (explicit assignment of two fields is clearer). The OpenAPI client regeneration happens automatically on `dotnet build` (Debug). Standard validation applies before completion: `dotnet build` + `dotnet format`; `npm run build` + `npm run lint`; and the Bank handler/controller tests touched by the change.
