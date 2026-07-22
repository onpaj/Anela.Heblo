## Goal (from the overall plan)

Retype the four date filter fields on `GetBankStatementListRequest` (and the matching `BankStatementsController` query parameters) from `string?` to `DateTime?`, deleting the now-redundant `DateTime.TryParse` logic duplicated across the handler and validator, and adapting the frontend client/hook to match.

This task is task 4 of 5. Tasks 1–3 (retype DTO/controller, remove handler parsing, simplify validator) are already done on this branch, and `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` succeeds. This task fixes the test project, which is still broken because it constructs `GetBankStatementListRequest` with string date literals.

---

### task: update-backend-unit-tests

**File:** `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs`

**Step 1 — confirm the test project currently fails to compile.**

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build fails with `CS0029`/`CS1503`-style errors in `GetBankStatementListHandlerTests.cs`, because `DateFrom = "2026-01-01"` (a `string`) can no longer be assigned to a `DateTime?` property. This confirms the "test" step of TDD for this task — the failure is the retype itself, already implemented in prior tasks; this task's job is to make the test file agree with the new types.

**Step 2 — update `Handle_PassesAllFilterFieldsToRepository`.**

Find:

```csharp
        var request = new GetBankStatementListRequest
        {
            TransferId = "  ABC  ",
            Account = "  shoptet  ",
            DateFrom = "2026-01-01",
            DateTo = "2026-01-31",
            ErrorsOnly = true,
        };
```

Replace with:

```csharp
        var request = new GetBankStatementListRequest
        {
            TransferId = "  ABC  ",
            Account = "  shoptet  ",
            DateFrom = new DateTime(2026, 1, 1),
            DateTo = new DateTime(2026, 1, 31),
            ErrorsOnly = true,
        };
```

The rest of the test (assertions on `captured.DateFrom`/`captured.DateTo` already use `new DateTime(2026, 1, 1)` / `new DateTime(2026, 1, 31)`) needs no change.

**Step 3 — delete `Handle_IgnoresUnparseableDateStrings`.**

Delete this entire test method (it is no longer reachable — a `DateTime?` property cannot hold an unparseable string, so the compiler rejects the scenario outright):

```csharp
    [Fact]
    public async Task Handle_IgnoresUnparseableDateStrings()
    {
        // Arrange
        var request = new GetBankStatementListRequest
        {
            DateFrom = "not-a-date",
            DateTo = "still-not-a-date",
        };
        BankStatementListFilter? captured = null;
        _repository
            .Setup(r => r.GetFilteredAsync(
                It.IsAny<BankStatementListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<BankStatementListFilter, int, int, string, bool, CancellationToken>(
                (f, _, _, _, _, _) => captured = f)
            .ReturnsAsync((Enumerable.Empty<BankStatementImport>(), 0));

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        captured!.DateFrom.Should().BeNull();
        captured.DateTo.Should().BeNull();
    }
```

`Handle_OmitsEmptyOrWhitespaceStringFilters` (no date fields involved) needs no change.

**Step 4 — delete `Validate_RejectsUnparseableDateFrom` and `Validate_RejectsUnparseableDateTo`.**

Delete both methods (same reason as Step 3 — the scenario can no longer be constructed):

```csharp
    [Fact]
    public void Validate_RejectsUnparseableDateFrom()
    {
        var request = new GetBankStatementListRequest { DateFrom = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }

    [Fact]
    public void Validate_RejectsUnparseableDateTo()
    {
        var request = new GetBankStatementListRequest { DateTo = "not-a-date" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateTo));
    }
```

**Step 5 — update `Validate_RejectsDateFromLaterThanDateTo`.**

Find:

```csharp
    [Fact]
    public void Validate_RejectsDateFromLaterThanDateTo()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-02-01", DateTo = "2026-01-01" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }
```

Replace with:

```csharp
    [Fact]
    public void Validate_RejectsDateFromLaterThanDateTo()
    {
        var request = new GetBankStatementListRequest { DateFrom = new DateTime(2026, 2, 1), DateTo = new DateTime(2026, 1, 1) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetBankStatementListRequest.DateFrom));
    }
```

**Step 6 — update `Validate_AcceptsValidDateRange`.**

Find:

```csharp
    [Fact]
    public void Validate_AcceptsValidDateRange()
    {
        var request = new GetBankStatementListRequest { DateFrom = "2026-01-01", DateTo = "2026-01-31" };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
```

Replace with:

```csharp
    [Fact]
    public void Validate_AcceptsValidDateRange()
    {
        var request = new GetBankStatementListRequest { DateFrom = new DateTime(2026, 1, 1), DateTo = new DateTime(2026, 1, 31) };
        var result = _validator.Validate(request);
        result.IsValid.Should().BeTrue();
    }
```

`Validate_RejectsTransferIdLongerThan100Chars`, `Validate_RejectsAccountLongerThan100Chars`, and `Validate_AcceptsAllNullOptionalFields` need no changes.

**Step 7 — run the scoped test suite.**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankStatementList"
```

Expected: all remaining tests in `GetBankStatementListHandlerTests` and `GetBankStatementListRequestValidatorTests` pass (7 tests: `Handle_PassesAllFilterFieldsToRepository`, `Handle_OmitsEmptyOrWhitespaceStringFilters`, `Validate_RejectsTransferIdLongerThan100Chars`, `Validate_RejectsAccountLongerThan100Chars`, `Validate_RejectsDateFromLaterThanDateTo`, `Validate_AcceptsAllNullOptionalFields`, `Validate_AcceptsValidDateRange`).

**Step 8 — add the optional model-binding integration test (recommended, precedent exists).**

`backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs` already exercises `GET /api/bank-statements/{id}` through a real `HttpClient` against `HebloWebApplicationFactory` (see `GetBankStatement_WithExistingId_Returns200WithDtoBody` / `GetBankStatement_WithMissingId_Returns404WithMessageBody`, both inside the `BankStatementImportIntegrationTests` class). This is a direct, in-file precedent for testing the list endpoint's new model-binding rejection path. Add a new test method to that same class, immediately after `GetBankStatement_WithMissingId_Returns404WithMessageBody` (i.e., still inside `BankStatementImportIntegrationTests`, before the closing brace of the class and before the `BankStatementImportTestFactory` class definition):

```csharp
    [Fact]
    public async Task GetBankStatements_WithInvalidDateFromQueryParam_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/bank-statements?dateFrom=not-a-date");

        // Assert — ASP.NET Core model binding rejects this before MediatR.Send runs.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
```

This requires no new `using` directives — `System.Net` (for `HttpStatusCode`) is already imported at the top of the file.

Run the whole integration test class to confirm it and everything around it still passes:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.BankStatementImportIntegrationTests"
```

Expected: all tests in the class pass, including the new one.

**Step 9 — full backend verification.**

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test
```

Expected: solution builds with 0 errors, formatting is clean, and the full backend test suite passes.

**Step 10 — commit.**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/GetBankStatementListHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs
git commit -m "Update GetBankStatementList backend tests for DateTime? fields; add 400 model-binding test"
```
