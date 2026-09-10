# Task brief: default-initialize-bank-account-settings-accounts

Working directory (already correct branch, do NOT create worktree/branch, do NOT switch branches):
`/Users/pajgrtondrej/orca/workspaces/Anela.Heblo/worktrees/feature-4061-Arch-Review-Bank-Bankaccountsettings-Accounts-Has`

Solution file for build/format/test commands: `Anela.Heblo.sln` at the worktree root (NOT `backend/Anela.Heblo.sln` — that path does not exist).

If `dotnet test` hangs at 0% CPU, another worktree may be running tests concurrently: run `dotnet build` first, then `dotnet test --no-build -p:UseSharedCompilation=false`.

## Files
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`
- Modify (test): `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs`

## Steps to execute, in order

1. Run baseline tests (from the worktree root):
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankAccountsHandlerTests|FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.ImportBankStatementHandlerTests"
```
Expected: PASS (5 tests in GetBankAccountsHandlerTests, 13 in ImportBankStatementHandlerTests).

2. In `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs`, change:
```csharp
    public List<BankAccountConfiguration> Accounts { get; set; }
```
to:
```csharp
    public List<BankAccountConfiguration> Accounts { get; set; } = new();
```

3. Re-run the same test filter as step 1. Expected: still PASS.

4. Commit:
```
git add backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs
git commit -m "fix: default-initialize BankAccountSettings.Accounts to an empty list

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SSpBtnSwW3QRii3BsqKnw4"
```

5. In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs`, change:
```csharp
        var accounts = (_bankSettings.Accounts ?? new List<BankAccountConfiguration>())
            .Select(a => new BankAccountDto
```
to:
```csharp
        var accounts = _bankSettings.Accounts
            .Select(a => new BankAccountDto
```
(keep the rest of the `.Select(...)` block and `.ToList()` unchanged — only remove the `(... ?? new List<BankAccountConfiguration>())` wrapper, replacing it with plain `_bankSettings.Accounts`).

6. In `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs`, delete this entire test method (it manufactures `Accounts = null!` to exercise the fallback branch just removed in step 5; its "no accounts → empty response" intent is already covered by `Handle_WithEmptyAccountsList_ReturnsEmptyResponse`):
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
After deletion, there should be exactly one blank line between the `CreateHandler` helper method's closing brace and the next `[Fact]` test (`Handle_WithEmptyAccountsList_ReturnsEmptyResponse`), matching the blank-line spacing already used between other adjacent `[Fact]` methods in the file.

7. Run:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankAccountsHandlerTests"
```
Expected: PASS — exactly 4 tests remain (`Handle_WithEmptyAccountsList_ReturnsEmptyResponse`, `Handle_WithConfiguredAccounts_MapsEachAccountToDto`, `Constructor_WithNullOptions_ThrowsArgumentNullException`, `Constructor_WithNullLogger_ThrowsArgumentNullException`).

8. Commit:
```
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs
git commit -m "fix: drop redundant null-guard in GetBankAccountsHandler

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SSpBtnSwW3QRii3BsqKnw4"
```

9. In `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`, change:
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

10. Run:
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.ImportBankStatementHandlerTests"
```
Expected: PASS — all 13 existing tests.

11. Commit:
```
git add backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs
git commit -m "fix: drop redundant null-guards in ImportBankStatementHandler

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SSpBtnSwW3QRii3BsqKnw4"
```

12. Full validation from the worktree root:
```
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
(If `dotnet test` hangs at 0% CPU: run `dotnet build Anela.Heblo.sln` then `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false`.)

Expected: build succeeds; `dotnet format --verify-no-changes` reports no changes needed; full test suite passes with no regressions.

If `dotnet format --verify-no-changes` reports unrelated PRE-EXISTING formatting drift elsewhere in the repo (not caused by your edits), do NOT run plain `dotnet format` across the whole solution to fix it — that would touch unrelated files, violating the "surgical changes" rule. Instead: run `dotnet format Anela.Heblo.sln` once, then `git diff --stat` to confirm it touched ONLY the 4 files listed above under "Files". If it touched anything else, `git checkout -- <that other file>` to revert the unrelated formatting change, keeping only the formatting fixes to your 4 files. Then commit just those:
```
git add backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs
git commit -m "style: apply dotnet format

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01SSpBtnSwW3QRii3BsqKnw4"
```
If format made no changes to these 4 files, skip this commit.

## Constraints
- Do NOT create a git worktree or new branch. Do NOT switch branches. Work directly in the given working directory on the currently checked-out branch.
- Only touch the 4 files listed above. Do not modify anything else (do not touch `artifacts/feat-4061/state.json` even though it may show as modified from before your work — leave it as-is, don't add or commit it).
- Do not run any subagents yourself — do the work directly.
- Every commit message MUST end with the two co-author/session lines shown above, after a blank line.

## Report

When done, write a full report to:
`/Users/pajgrtondrej/orca/workspaces/Anela.Heblo/worktrees/feature-4061-Arch-Review-Bank-Bankaccountsettings-Accounts-Has/artifacts/feat-4061/impl/task-report.md`

covering: what you changed, exact test commands run and their pass/fail counts, the build/format results, and the list of commits you made (hash + message). Then return a short status: DONE, DONE_WITH_CONCERNS, NEEDS_CONTEXT, or BLOCKED, plus the commit hashes.
