## Goal (from the overall plan)

Retype the four date filter fields on `GetBankStatementListRequest` (and the matching `BankStatementsController` query parameters) from `string?` to `DateTime?`, deleting the now-redundant `DateTime.TryParse` logic duplicated across the handler and validator, and adapting the frontend client/hook to match.

This task is task 2 of 5. Task 1 (`retype-request-and-controller-date-fields`) already retyped `GetBankStatementListRequest`'s four date fields to `DateTime?` and the controller's matching `[FromQuery]` params — that part is DONE on this branch already (check with `git log` if unsure). This task removes the now-obsolete manual parsing in the handler that those retyped fields make redundant.

**Compilation-order note:** the `Application`/`API` projects will still **not** compile after this task — `GetBankStatementListRequestValidator.cs` still calls `BeParseableDate(x.DateFrom)` expecting a `string?`, which now fails (`CS1503`). That's expected and fixed by the next task (`simplify-validator-date-rules`). Do not attempt `dotnet build` in this task.

---

### task: remove-handler-date-parsing

**File:** `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs`

**Step 1 — remove the parsing calls and the helper.**

Current `Handle` body and helper:

```csharp
    public async Task<GetBankStatementListResponse> Handle(GetBankStatementListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement list with Skip={Skip}, Take={Take}", request.Skip, request.Take);

        DateTime? statementDate = ParseDateOrNull(request.StatementDate);
        DateTime? importDate = ParseDateOrNull(request.ImportDate);
        DateTime? dateFrom = ParseDateOrNull(request.DateFrom);
        DateTime? dateTo = ParseDateOrNull(request.DateTo);

        var trimmedTransferId = NormalizeNullableString(request.TransferId);
        var trimmedAccount = NormalizeNullableString(request.Account);

        var filter = new BankStatementListFilter(
            Id: request.Id,
            TransferId: trimmedTransferId,
            Account: trimmedAccount,
            StatementDate: statementDate,
            ImportDate: importDate,
            DateFrom: dateFrom,
            DateTo: dateTo,
            ErrorsOnly: request.ErrorsOnly);

        var (items, totalCount) = await _repository.GetFilteredAsync(
            filter,
            skip: request.Skip,
            take: request.Take,
            orderBy: request.OrderBy ?? "ImportDate",
            ascending: request.Ascending,
            cancellationToken: cancellationToken);

        var dtoList = _mapper.Map<List<BankStatementImportDto>>(items);

        _logger.LogInformation("Retrieved {Count} bank statements (total: {TotalCount})", dtoList.Count, totalCount);

        return new GetBankStatementListResponse
        {
            Items = dtoList,
            TotalCount = totalCount
        };
    }

    private static DateTime? ParseDateOrNull(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out var parsed) ? parsed : null;

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

Replace with (removes the 4 local variables and the `ParseDateOrNull` method; passes `request.StatementDate`/`request.ImportDate`/`request.DateFrom`/`request.DateTo` straight into the filter constructor; `NormalizeNullableString` and its two call sites are untouched):

```csharp
    public async Task<GetBankStatementListResponse> Handle(GetBankStatementListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting bank statement list with Skip={Skip}, Take={Take}", request.Skip, request.Take);

        var trimmedTransferId = NormalizeNullableString(request.TransferId);
        var trimmedAccount = NormalizeNullableString(request.Account);

        var filter = new BankStatementListFilter(
            Id: request.Id,
            TransferId: trimmedTransferId,
            Account: trimmedAccount,
            StatementDate: request.StatementDate,
            ImportDate: request.ImportDate,
            DateFrom: request.DateFrom,
            DateTo: request.DateTo,
            ErrorsOnly: request.ErrorsOnly);

        var (items, totalCount) = await _repository.GetFilteredAsync(
            filter,
            skip: request.Skip,
            take: request.Take,
            orderBy: request.OrderBy ?? "ImportDate",
            ascending: request.Ascending,
            cancellationToken: cancellationToken);

        var dtoList = _mapper.Map<List<BankStatementImportDto>>(items);

        _logger.LogInformation("Retrieved {Count} bank statements (total: {TotalCount})", dtoList.Count, totalCount);

        return new GetBankStatementListResponse
        {
            Items = dtoList,
            TotalCount = totalCount
        };
    }

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

**Step 2 — expected build state.**

Still do **not** attempt `dotnet build`. `GetBankStatementListRequestValidator.cs` still calls `BeParseableDate(x.DateFrom)` where `BeParseableDate` expects `string?` — this still fails to compile (`CS1503`). The next task fixes it.

**Step 3 — commit.**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementList/GetBankStatementListHandler.cs
git commit -m "Remove redundant ParseDateOrNull helper from GetBankStatementListHandler"
```
