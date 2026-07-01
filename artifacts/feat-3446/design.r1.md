# Design: Surface bank statement import outcome counts (success / error) end-to-end

## Component Design

- **`ImportBankStatementHandler`** (`Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`) — unchanged responsibility (process bank statements, compute `successCount`/`errorCount`). Change: assign the already-computed locals onto the returned `ImportBankStatementResponse` instead of discarding them after the log line. No change to the counting rule.

- **`ImportBankStatementResponse`** (`.../ImportBankStatement/ImportBankStatementResponse.cs`) — gains settable `SuccessCount` (int) and `ErrorCount` (int) properties alongside the existing `Statements`. Continues to inherit `BaseResponse` (`Success`/`ErrorCode`/`Params`), consistent with every other Bank use case response.

- **`BankStatementsController.ImportStatements`** (`API/Controllers/BankStatementsController.cs:52-55`) — stays a thin MediatR orchestrator. Change: copies `SuccessCount` and `ErrorCount` from the handler response onto `BankStatementImportResultDto` alongside `Statements` (explicit field assignment, no AutoMapper needed). `TotalCount`/`HasErrors` are not set explicitly — they derive on the DTO itself. Error paths (`ArgumentException` → 400, generic → 500) unchanged.

- **`BankStatementImportResultDto`** (`Application/Features/Bank/Contracts/BankStatementImportResultDto.cs`) — plain C# class (never a record). Gains `SuccessCount`, `ErrorCount` as settable auto-properties, and `TotalCount`/`HasErrors` as computed read-only properties derived from the other fields (`TotalCount => Statements.Count`, `HasErrors => ErrorCount > 0`). Fallback if NSwag mishandles computed properties: make them plain settable properties populated by the controller — verify the generated TypeScript client after `dotnet build` either way.

- **`useBankStatements.ts`** (`frontend/src/api/hooks/useBankStatements.ts`) — hand-written `BankImportResponse` interface (and the unused `BankStatementImportResult`) extended with `successCount`, `errorCount`, `totalCount`, `hasErrors` (camelCase, matching JSON serialization). This is the type `ImportTab.tsx` actually consumes (not the generated client).

- **`ImportTab.tsx`** (`handleImportSubmit`, lines ~159-166) — reads the new fields from the mutation response and branches the existing `alert(...)` message: fully successful (`hasErrors === false`, `totalCount > 0`) reports `successCount`; zero statements (`totalCount === 0`) reports nothing-to-import; partial failure (`hasErrors === true`) reports both `successCount` and `errorCount` with an explicit failure indication. No new component, no new UI mechanism — this is a string/branching change only.

- Generated OpenAPI clients (`frontend/src/api/generated/api-client.ts`, `backend/src/Anela.Heblo.API.Client/Generated/AnelaHebloApiClient.cs`) regenerate on build from the updated DTO; not hand-edited.

## Data Schemas

No persistence or schema changes; all types are in-memory DTOs.

**`BankStatementImportResultDto`** (C#, class):
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

**`ImportBankStatementResponse`** — same `SuccessCount`/`ErrorCount` settable properties added; handler populates from existing locals:
```csharp
return new ImportBankStatementResponse
{
    Statements = imports,
    SuccessCount = successCount,   // imports.Count(i => i.ImportResult == ImportStatus.Success)
    ErrorCount = errorCount,       // imports.Count - successCount
};
```

**`POST /api/bank-statements/import` response body** (extended, additive — no breaking change):
```json
{
  "statements": [ /* BankStatementImportDto[] — unchanged */ ],
  "successCount": 3,
  "errorCount": 1,
  "totalCount": 4,
  "hasErrors": true
}
```
Status codes unchanged: 200 success, 400 `ArgumentException`, 500 unexpected error.

**Frontend `BankImportResponse` / `BankStatementImportResult`** (`useBankStatements.ts`):
```ts
export interface BankImportResponse {
  statements: BankStatementImportDto[];
  successCount: number;
  errorCount: number;
  totalCount: number;
  hasErrors: boolean;
}
```

Counting invariants (unchanged rule, now surfaced): `TotalCount == Statements.Count`; `SuccessCount == count(s => s.ImportResult == "OK")`; `ErrorCount == TotalCount - SuccessCount`; `HasErrors == ErrorCount > 0`. `SkippedCount` is intentionally omitted — there is no skip concept in the pipeline.
