# Default-initialize BankAccountSettings.Accounts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `BankAccountSettings.Accounts` a default `= new();` initializer so it is never `null`, then remove the now-redundant null-guards in its two callers (`GetBankAccountsHandler`, `ImportBankStatementHandler`).

**Architecture:** Pure internal simplification, no new components. One property gets a default initializer in the Domain layer; the two Application-layer handlers that previously defended against a `null` `Accounts` list read it directly. An existing test that manufactures the now-unsupported "forced null" state is removed since it exercises exactly the defensive branch being deleted, and is already redundant with an existing "empty list" test that covers the same observable behavior.

**Tech Stack:** .NET 8, C#, xUnit, Moq, FluentAssertions (existing `Anela.Heblo.Tests` project).

---

## File Structure

All changes are edits to existing files — nothing is created or deleted at the file level:

- `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs` — add `= new();` to `Accounts`.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs` — drop the `?? new List<BankAccountConfiguration>()` fallback on line 24.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — drop the `?.` on line 54 and the `!= null ? ... : "None"` conditional on lines 57–59.
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs` — remove the `Handle_WithNullAccountsList_ReturnsEmptyResponse` test (lines 25–37), which forces `Accounts = null!` and would throw a `NullReferenceException` once the handler's null-guard is removed. Its coverage (no accounts configured → empty response) is already fully provided by the adjacent `Handle_WithEmptyAccountsList_ReturnsEmptyResponse` test.
- `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` — **no change**. Its constructor always populates `Accounts` with two entries, and the only existing assertion touching the "account not found" path (`Handle_WithUnknownAccount_ThrowsArgumentException`) checks `exception.Message` contains `"Account name UNKNOWN not found"`, which does not cover the `"None"` → `""` wording change from FR-3. Verified via repo-wide search: no test anywhere asserts on the literal `"None"` or on `"Available accounts"`.

## Scope Check

This spec is a single, tiny, self-contained cleanup (three production-file edits plus one obsolete-test removal). It does not need to be split into sub-project specs.

---

### task: default-initialize-bank-account-settings-accounts

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs:7`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs:24`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs:54,57-59`
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs:25-37`

- [ ] **Step 1: Confirm the baseline — run the existing Bank tests before touching anything**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankAccountsHandlerTests|FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.ImportBankStatementHandlerTests"
```
Expected: PASS (all 5 tests in `GetBankAccountsHandlerTests` and all 13 tests in `ImportBankStatementHandlerTests` currently pass). This establishes the safety net before the refactor.

- [ ] **Step 2: Add the default initializer to `BankAccountSettings.Accounts`**

Current content of `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public class BankAccountSettings
{
    public const string ConfigurationKey = "BankAccounts";

    public List<BankAccountConfiguration> Accounts { get; set; }
}
```

Change the `Accounts` property to:

```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public class BankAccountSettings
{
    public const string ConfigurationKey = "BankAccounts";

    public List<BankAccountConfiguration> Accounts { get; set; } = new();
}
```

- [ ] **Step 3: Run the Bank tests again to confirm this step alone is non-breaking**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankAccountsHandlerTests|FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.ImportBankStatementHandlerTests"
```
Expected: PASS. `Handle_WithNullAccountsList_ReturnsEmptyResponse` still passes at this point: the test's object initializer (`Accounts = null!`) explicitly overwrites the new default value, and `GetBankAccountsHandler` still has its `?? new List<BankAccountConfiguration>()` fallback (removed in Step 5), so the handler still tolerates the forced-null state for now.

- [ ] **Step 4: Commit the default initializer**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs
git commit -m "fix: default-initialize BankAccountSettings.Accounts to an empty list"
```

- [ ] **Step 5: Remove the null fallback in `GetBankAccountsHandler`**

In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs`, change:

```csharp
    public Task<GetBankAccountsResponse> Handle(GetBankAccountsRequest request, CancellationToken cancellationToken)
    {
        var accounts = (_bankSettings.Accounts ?? new List<BankAccountConfiguration>())
            .Select(a => new BankAccountDto
            {
                Name = a.Name,
                AccountNumber = a.AccountNumber,
                Provider = a.Provider.ToString(),
                Currency = a.Currency.ToString(),
            })
            .ToList();
```

to:

```csharp
    public Task<GetBankAccountsResponse> Handle(GetBankAccountsRequest request, CancellationToken cancellationToken)
    {
        var accounts = _bankSettings.Accounts
            .Select(a => new BankAccountDto
            {
                Name = a.Name,
                AccountNumber = a.AccountNumber,
                Provider = a.Provider.ToString(),
                Currency = a.Currency.ToString(),
            })
            .ToList();
```

- [ ] **Step 6: Remove the now-obsolete forced-null test from `GetBankAccountsHandlerTests`**

Removing Step 5's fallback means `_bankSettings.Accounts.Select(...)` throws a `NullReferenceException` if `Accounts` is `null`. The test below manufactures exactly that (now-unsupported) state via an object initializer that overwrites the class's own default value — it exists purely to exercise the fallback branch just deleted, and its "no accounts configured → empty response" intent is already covered by `Handle_WithEmptyAccountsList_ReturnsEmptyResponse` immediately below it. Delete it.

In `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs`, remove this whole test (lines 25–37):

```csharp
    [Fact]
    public async Task Handle_WithNullAccountsList_ReturnsEmptyResponse()
    {
        var settings = new BankAccountSettings { Accounts = null! };
        var handler = CreateHandler(settings);

        var response = await handler.Handle(new GetBankAccountsRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.Accounts);
        Assert.Empty(response.Accounts);
        Assert.True(response.Success);
    }

```

(Leave the blank line pattern consistent with the surrounding file — i.e. there should be exactly one blank line between the `CreateHandler` helper method's closing brace and the `Handle_WithEmptyAccountsList_ReturnsEmptyResponse` test that follows, exactly as it already is between any two adjacent `[Fact]` methods in this file.)

- [ ] **Step 7: Run the `GetBankAccountsHandlerTests` to verify Steps 5–6 pass together**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankAccountsHandlerTests"
```
Expected: PASS — 4 tests (`Handle_WithEmptyAccountsList_ReturnsEmptyResponse`, `Handle_WithConfiguredAccounts_MapsEachAccountToDto`, `Constructor_WithNullOptions_ThrowsArgumentNullException`, `Constructor_WithNullLogger_ThrowsArgumentNullException`).

- [ ] **Step 8: Commit the `GetBankAccountsHandler` simplification**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs
git commit -m "fix: drop redundant null-guard in GetBankAccountsHandler"
```

- [ ] **Step 9: Remove the null-conditional operators in `ImportBankStatementHandler`**

In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`, change:

```csharp
        var accountSetting = _bankSettings.Accounts?.SingleOrDefault(a => a.Name == request.AccountName);
        if (accountSetting == null)
        {
            var availableAccounts = _bankSettings.Accounts != null
                ? string.Join(", ", _bankSettings.Accounts.Select(a => a.Name))
                : "None";

            _logger.LogError(
                "Bank import FAILED - Account not found: {AccountName}. Available accounts: {AvailableAccounts}",
                request.AccountName, availableAccounts);

            throw new ArgumentException(
                $"Account name {request.AccountName} not found in {BankAccountSettings.ConfigurationKey} configuration. Available accounts: {availableAccounts}");
        }
```

to:

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

- [ ] **Step 10: Run the `ImportBankStatementHandlerTests` to verify Step 9 passes**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.ImportBankStatementHandlerTests"
```
Expected: PASS — all 13 existing tests, including `Handle_WithUnknownAccount_ThrowsArgumentException`, which only asserts the message contains `"Account name UNKNOWN not found"` and is unaffected by the `"None"` → `""` wording change in the "Available accounts" segment (this handler's test fixture always configures two accounts, so the zero-accounts wording path isn't even exercised here).

- [ ] **Step 11: Commit the `ImportBankStatementHandler` simplification**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs
git commit -m "fix: drop redundant null-guards in ImportBankStatementHandler"
```

- [ ] **Step 12: Full validation — build, format, and full test suite**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: build succeeds with no new warnings; `dotnet format` reports no changes needed; the full test suite passes (no regressions outside the Bank feature — nothing else in the codebase reads `BankAccountSettings.Accounts`).

If `dotnet format --verify-no-changes` reports unrelated pre-existing formatting drift, run `dotnet format backend/Anela.Heblo.sln` without `--verify-no-changes` and confirm via `git diff` that it touches only the files edited in this task before committing; do not let it reformat unrelated files.

- [ ] **Step 13: Final commit if Step 12 made formatting changes**

Only if Step 12's `dotnet format` altered any of this task's files:

```bash
git add backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs
git commit -m "style: apply dotnet format"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (default-initialize `Accounts`) → Step 2.
- FR-2 (simplify `GetBankAccountsHandler`) → Step 5, verified by Step 7.
- FR-3 (simplify `ImportBankStatementHandler`) → Step 9, verified by Step 10.
- FR-3's accepted `"None"` → `""` wording side effect → confirmed in Step 10's expected-result note (no test asserts on it).
- Arch-review Risk 1 ("an existing test asserts on the literal `None` string") → confirmed not present via repo-wide grep; noted in File Structure and Step 10.
- Arch-review Risk 2 ("a test directly constructs `new BankAccountSettings()` and asserts `Accounts == null`") → found the related-but-distinct case (a test forcing `Accounts = null!` via object initializer and expecting graceful handling) and handled it in Step 6.
- Out-of-scope items (DI/registration, `BankAccountConfiguration` shape, other handlers, logging levels/exception types beyond FR-3's accepted wording change) → untouched by this plan.

**2. Placeholder scan:** No TBD/TODO markers; every step shows exact before/after code and exact commands with expected output.

**3. Type consistency:** `BankAccountSettings.Accounts` stays `List<BankAccountConfiguration>` throughout; `GetBankAccountsResponse`, `BankAccountDto`, and the `ArgumentException` message format are unchanged except for the accepted FR-3 wording effect.
