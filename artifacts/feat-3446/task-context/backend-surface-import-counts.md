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
