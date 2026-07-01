# Surface Bank Statement Import Outcome Counts Implementation Plan

**Goal:** Surface the per-run success/error counts the import handler already computes through the response contract, controller, and `ImportTab.tsx` so operators can tell a partial-failure import from a full success.

**Architecture:** One vertical slice (`Bank`) touched at three backend points (Contracts DTO, use-case Response, Handler + Controller pass-through) plus two frontend points (hand-written hook types + `ImportTab.tsx` alert branching). The counting rule is unchanged — the handler already has the numbers; we stop discarding them. `ImportBankStatementResponse : BaseResponse` is kept (consistent with every other Bank use case) and the controller copies all fields, so no lossy projection remains. `TotalCount`/`HasErrors` are derived read-only properties, not stored.

**Tech Stack:** .NET 8 (MediatR, AutoMapper, xUnit + Moq), React + TypeScript (react-query, hand-written fetch hook), NSwag OpenAPI client generation on build.

---

### task: backend-surface-import-counts

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportResultDto.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs:120`
- Modify: `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs:52-55`
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`

This task adds the outcome counts to the backend contract and populates them, keeping the counting rule byte-for-byte identical (only assigning already-computed locals instead of discarding them). Follow TDD: write the failing handler test first, then make the four production edits.

**Step 1 — write the failing test.** Append the following two tests to `ImportBankStatementHandlerTests.cs` (inside the existing `ImportBankStatementHandlerTests` class, after `Handle_WithValidAccount_ResolvesClientViaFactory`). They exercise a mixed run (2 OK, 1 error) and an empty run, asserting the new response fields. The `_mockMapper` currently returns `null` for `Map<BankStatementImportDto>`, so set it up to echo the saved entity's `ImportResult` into a real DTO. Note `IBankStatementImportService.ImportStatementAsync(int accountId, string statementData)` returns `Result<bool>` (`Result.Success(true)` / `Result<bool>.Failure("...")`), and `IBankClient.GetStatementAsync(string)` returns `BankStatementData` (`{ StatementId, ItemCount, Data }`). `_mockRepository.AddAsync` must echo its argument back so the handler maps a saved entity with the `ImportResult` it set.

```csharp
    [Fact]
    public async Task Handle_WithMixedResults_PopulatesSuccessAndErrorCounts()
    {
        var dateFrom = DateTime.Today.AddDays(-1);
        var dateTo = DateTime.Today;
        var request = new ImportBankStatementRequest("ComgateCZK", dateFrom, dateTo);

        var headers = new List<BankStatementHeader>
        {
            new BankStatementHeader { StatementId = "S1", Date = dateTo, Account = "123456789" },
            new BankStatementHeader { StatementId = "S2", Date = dateTo, Account = "123456789" },
            new BankStatementHeader { StatementId = "S3", Date = dateTo, Account = "123456789" },
        };

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", dateFrom, dateTo))
            .ReturnsAsync(headers);
        _mockBankClient.Setup(x => x.GetStatementAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new BankStatementData { StatementId = id, ItemCount = 1, Data = "abo" });

        // S1, S2 succeed; S3 fails.
        _mockImportService
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success(true));
        _mockImportService
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), "abo"))
            .Returns<int, string>((_, _) => Task.FromResult(Result<bool>.Success(true)));

        // Override so that S3's ItemCount triggers failure path deterministically:
        // easier: fail based on statement id via GetStatementAsync data.
        _mockBankClient.Setup(x => x.GetStatementAsync("S3"))
            .ReturnsAsync(new BankStatementData { StatementId = "S3", ItemCount = 1, Data = "fail" });
        _mockImportService
            .Setup(x => x.ImportStatementAsync(It.IsAny<int>(), "fail"))
            .ReturnsAsync(Result<bool>.Failure("PROCESSING_ERROR"));

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<BankStatementImport>()))
            .ReturnsAsync((BankStatementImport e) => e);
        _mockMapper.Setup(x => x.Map<BankStatementImportDto>(It.IsAny<BankStatementImport>()))
            .Returns((BankStatementImport e) => new BankStatementImportDto { ImportResult = e.ImportResult });

        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.Equal(3, response.Statements.Count);
        Assert.Equal(2, response.SuccessCount);
        Assert.Equal(1, response.ErrorCount);
    }

    [Fact]
    public async Task Handle_WithNoStatements_ReturnsZeroCounts()
    {
        var dateFrom = DateTime.Today.AddDays(-1);
        var dateTo = DateTime.Today;
        var request = new ImportBankStatementRequest("ComgateCZK", dateFrom, dateTo);

        _mockBankClient.Setup(x => x.GetStatementsAsync("123456789", dateFrom, dateTo))
            .ReturnsAsync(new List<BankStatementHeader>());

        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.Empty(response.Statements);
        Assert.Equal(0, response.SuccessCount);
        Assert.Equal(0, response.ErrorCount);
    }
```

Add the required using at the top of the test file if not already present: `using Anela.Heblo.Application.Features.Bank.Contracts;` (needed for `BankStatementImportDto`). `Anela.Heblo.Domain.Shared` (for `Result`) and `Anela.Heblo.Domain.Features.Bank` are already imported.

Run it (expect compile failure — `SuccessCount`/`ErrorCount` do not exist yet):
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests"
```

**Step 2 — extend the DTO.** Replace the full contents of `BankStatementImportResultDto.cs` with (keep it a plain class, never a record):

```csharp
namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankStatementImportResultDto
{
    public List<BankStatementImportDto> Statements { get; set; } = new List<BankStatementImportDto>();
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public int TotalCount => Statements.Count;
    public bool HasErrors => ErrorCount > 0;
}
```

**Step 3 — extend the response.** Replace the full contents of `ImportBankStatementResponse.cs` with:

```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;

public class ImportBankStatementResponse : BaseResponse
{
    public List<BankStatementImportDto> Statements { get; set; } = new List<BankStatementImportDto>();
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
}
```

**Step 4 — populate in the handler.** In `ImportBankStatementHandler.cs`, the counting locals `successCount` (line 113) and `errorCount` (line 114) and the log line are unchanged. Only change the `return` on line 120. Replace:

```csharp
        return new ImportBankStatementResponse { Statements = imports };
```
with:
```csharp
        return new ImportBankStatementResponse
        {
            Statements = imports,
            SuccessCount = successCount,
            ErrorCount = errorCount,
        };
```

Do not touch lines 113–118; the counting rule (`successCount = imports.Count(i => i.ImportResult == ImportStatus.Success)`, `errorCount = imports.Count - successCount`) and the `Bank import COMPLETED` log line must report the identical numbers.

**Step 5 — copy fields in the controller.** In `BankStatementsController.cs`, replace the mapping at lines 52–55:

```csharp
            var result = new BankStatementImportResultDto
            {
                Statements = response.Statements
            };
```
with:
```csharp
            var result = new BankStatementImportResultDto
            {
                Statements = response.Statements,
                SuccessCount = response.SuccessCount,
                ErrorCount = response.ErrorCount,
            };
```

`TotalCount` and `HasErrors` derive on the DTO and must not be assigned. The `try/catch` error paths (`ArgumentException` → 400, generic → 500) stay untouched.

**Step 6 — verify.** Run:
```
cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests" && dotnet format --verify-no-changes
```
`dotnet build` (Debug) also regenerates the NSwag TypeScript client. After building, confirm the generated `frontend/src/api/generated/api-client.ts` exposes `successCount`, `errorCount`, `totalCount`, `hasErrors` on the import result type. If NSwag omitted the computed `totalCount`/`hasErrors` (rare generator quirk), fall back per Decision 2: convert them to plain settable auto-properties on `BankStatementImportResultDto` (`public int TotalCount { get; set; }` / `public bool HasErrors { get; set; }`) and assign them explicitly in the controller (`TotalCount = response.Statements.Count`, `HasErrors = response.ErrorCount > 0`), then rebuild. Do not hand-edit generated files.

Commit message: `fix(bank): surface import success/error counts in import result contract`

### task: frontend-surface-import-outcome

**Files:**
- Modify: `frontend/src/api/hooks/useBankStatements.ts:168-174`
- Modify: `frontend/src/components/customer/tabs/ImportTab.tsx:159-166`

This task extends the hand-written response types and branches the completion alert. It depends on `backend-surface-import-counts` producing the fields in the JSON response, but requires no generated-client change (`ImportTab.tsx` reads the hand-written `BankImportResponse`, not the generated client).

**Step 1 — extend the hook types.** In `useBankStatements.ts`, replace the two interfaces at lines 168–174:

```typescript
export interface BankStatementImportResult {
  statements: BankStatementImportDto[];
}

export interface BankImportResponse {
  statements: BankStatementImportDto[];
}
```
with:
```typescript
export interface BankStatementImportResult {
  statements: BankStatementImportDto[];
  successCount: number;
  errorCount: number;
  totalCount: number;
  hasErrors: boolean;
}

export interface BankImportResponse {
  statements: BankStatementImportDto[];
  successCount: number;
  errorCount: number;
  totalCount: number;
  hasErrors: boolean;
}
```

**Step 2 — branch the alert.** In `ImportTab.tsx`, `handleImportSubmit` (lines 159–166) currently discards the mutation result and shows a fixed alert. Replace:

```typescript
      // Single import request for the selected date
      await importMutation.mutateAsync({
        accountName: selectedAccount,
        dateFrom: importDate,
        dateTo: importDate,
      });

      // Show success message
      alert(`Import dokončen pro datum ${importDate}`);
```
with:
```typescript
      // Single import request for the selected date
      const result = await importMutation.mutateAsync({
        accountName: selectedAccount,
        dateFrom: importDate,
        dateTo: importDate,
      });

      // Show outcome message reflecting the per-run counts
      if (result.totalCount === 0) {
        alert(`Import dokončen pro datum ${importDate}: žádné výpisy k importu.`);
      } else if (result.hasErrors) {
        alert(
          `Import dokončen pro datum ${importDate}: ${result.successCount} úspěšně, ${result.errorCount} s chybou. Zkontrolujte seznam výpisů.`
        );
      } else {
        alert(`Import dokončen pro datum ${importDate}: ${result.successCount} výpisů úspěšně naimportováno.`);
      }
```

The surrounding lines (`refetch()`, `setShowImportModal(false)`, `resetImportForm()`, the `catch`/`finally` blocks) are unchanged.

**Step 3 — verify.** Run:
```
cd frontend && npm run build && npm run lint
```
Both must pass. Manually confirm the three branches read correctly: zero statements → "žádné výpisy k importu"; `hasErrors` true → both counts + "Zkontrolujte seznam výpisů"; otherwise → success count.

Commit message: `feat(bank): show import success/error counts in ImportTab completion message`
