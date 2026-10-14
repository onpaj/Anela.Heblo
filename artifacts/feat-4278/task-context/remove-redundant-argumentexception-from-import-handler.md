### task: remove-redundant-argumentexception-from-import-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs:50-61`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs:88-97`

- [ ] **Step 1: Update the handler test file first — remove the now-invalid test**

In `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs`, delete this test (the handler will no longer throw `ArgumentException` for an unknown account — that responsibility now belongs to `ImportBankStatementRequestValidator`, already covered by `ImportBankStatementRequestValidatorTests.AccountName_Unknown_ShouldHaveValidationError` from the previous task):

```csharp
    [Fact]
    public async Task Handle_WithUnknownAccount_ThrowsArgumentException()
    {
        var request = new ImportBankStatementRequest("UNKNOWN", DateTime.Today, DateTime.Today);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _handler.Handle(request, CancellationToken.None));

        Assert.Contains("Account name UNKNOWN not found", exception.Message);
    }
```

Do not remove any other test in this file — `Constructor_WithNullFactory_ThrowsArgumentNullException` and `Handle_WithValidAccount_ResolvesClientViaFactory` (and any tests below them) stay exactly as they are.

- [ ] **Step 2: Run the handler tests to confirm the suite still compiles and the rest still passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests"`
Expected: PASS — all remaining tests green (the deleted test is simply gone from the run).

- [ ] **Step 3: Remove the handler's manual `ArgumentException` guard**

In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`, replace:

```csharp
        var accountSetting = _bankSettings.Accounts.SingleOrDefault(a => a.Name == request.AccountName);
        if (accountSetting == null)
        {
            var availableAccounts = string.Join(", ", _bankSettings.Accounts.Select(a => a.Name));

            _logger.LogError(
                "Bank import FAILED - Account not found: {AccountName}. Available accounts: {AvailableAccounts}",
                request.AccountName, availableAccounts);

            throw new ArgumentException(
                $"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
        }
```

with:

```csharp
        var accountSetting = _bankSettings.Accounts.Single(a => a.Name == request.AccountName);
```

Nothing else in this file changes — the rest of `Handle`, `ProcessStatementAsync`, `InsertNewAsync`, and `UpsertExistingAsync` remain untouched.

- [ ] **Step 4: Run the full handler test suite to verify it still passes after the handler change**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportBankStatementHandlerTests"`
Expected: PASS — all tests green.

- [ ] **Step 5: Run the full Bank feature test suite (validator + handler + everything else in the module) to confirm no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank"`
Expected: PASS — all tests green, including `ImportBankStatementRequestValidatorTests` from the first task and `ImportBankStatementHandlerTests`.

- [ ] **Step 6: Check for any other test asserting the old 500/ArgumentException behavior at the controller or integration level**

Run: `grep -n "ArgumentException\|UNKNOWN" backend/test/Anela.Heblo.Tests/Controllers/BankStatementsControllerTests.cs backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementImportIntegrationTests.cs`

If this returns any match referencing an unknown-account/`ArgumentException` scenario for the import endpoint, update that test's expectation to match the new behavior (an unknown account now causes `FluentValidation.ValidationException` to be thrown before the handler runs, which `ValidationExceptionHandler` maps to HTTP 400 `ProblemDetails` — not a 500 or a raw `ArgumentException`). If it returns no match, no further action is needed for this step.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs
git commit -m "fix(bank): remove redundant ArgumentException now that validation guards ImportBankStatementRequest"
```

---
