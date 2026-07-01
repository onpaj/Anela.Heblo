## Module
Bank

## Finding
`BankStatementsController.ImportStatements` (`backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs:50–55`) discards four fields from the handler's response when building the API reply:

```csharp
var result = new BankStatementImportResultDto
{
    Statements = response.Statements   // SuccessCount, ErrorCount, SkippedCount, HasErrors dropped
};
return Ok(result);
```

`ImportBankStatementResponse` carries `SuccessCount`, `ErrorCount`, `SkippedCount`, and `HasErrors`. `BankStatementImportResultDto` (`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportResultDto.cs`) only exposes `Statements`.

The frontend (`ImportTab.tsx:165–168`) ignores the response body entirely and shows a generic "Import completed for date X" alert, so a partial run (e.g. 3 statements OK, 1 failed) is indistinguishable from a fully successful one.

## Why it matters
- **Business logic guideline**: the controller is doing a lossy DTO transformation that belongs in the contract layer.
- **User-visible impact**: operators cannot tell from the UI whether any statements failed during a manual import without drilling into the statement list and looking for error rows.
- The handler already computes and returns the information — it is simply never surfaced.

## Suggested fix
Add `SuccessCount`, `ErrorCount`, `SkippedCount` fields to `BankStatementImportResultDto` and populate them from `ImportBankStatementResponse` in the controller (or have the handler return `BankStatementImportResultDto` directly). Update `ImportTab.tsx` to display the counts in the success message.

---
_Filed by daily arch-review routine on 2026-07-01._
