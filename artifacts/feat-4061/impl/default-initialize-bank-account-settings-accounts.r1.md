# Implementation: default-initialize-bank-account-settings-accounts

## What was implemented

`BankAccountSettings.Accounts` (`List<BankAccountConfiguration>`) is now default-initialized to `new()`, so the property can never be `null`. This removed the need for defensive null-handling in its two consumers:

- `GetBankAccountsHandler` no longer wraps `_bankSettings.Accounts` in a `?? new List<BankAccountConfiguration>()` fallback.
- `ImportBankStatementHandler` no longer uses the null-conditional operator (`Accounts?.SingleOrDefault(...)`) or the `Accounts != null ? ... : "None"` ternary when building the "account not found" error message — it now always lists the configured account names via a plain `string.Join(...)` (an empty string, not `"None"`, is produced if no accounts are configured; no test asserts on that specific wording).

The now-obsolete unit test `Handle_WithNullAccountsList_ReturnsEmptyResponse` (which forced `Accounts = null!` via an object initializer to exercise the removed fallback branch) was deleted from `GetBankAccountsHandlerTests.cs`; its "no accounts configured → empty response" intent remains covered by `Handle_WithEmptyAccountsList_ReturnsEmptyResponse`.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs` — `Accounts` property now defaults to `new()`.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs` — dropped the null-coalescing fallback.
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — dropped both null-guards around `Accounts`.
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs` — removed the obsolete null-list test.

Exactly these 4 files were touched (verified via `git diff --stat` across all 3 commits); nothing else in the repo reads `BankAccountSettings.Accounts`.

## Tests

- `GetBankAccountsHandlerTests.cs` — 4 tests remain (`Handle_WithEmptyAccountsList_ReturnsEmptyResponse`, `Handle_WithConfiguredAccounts_MapsEachAccountToDto`, `Constructor_WithNullOptions_ThrowsArgumentNullException`, `Constructor_WithNullLogger_ThrowsArgumentNullException`) — all pass.
- `ImportBankStatementHandlerTests.cs` — unchanged, 15 `[Fact]`/`[Theory]` methods (the task brief estimated 13; actual count in the repo is 15 — a pre-existing discrepancy in the task description, not something the implementation affected), all pass, including `Handle_WithUnknownAccount_ThrowsArgumentException`.
- Combined targeted run: `dotnet test ... --filter "FullyQualifiedName~GetBankAccountsHandlerTests|FullyQualifiedName~ImportBankStatementHandlerTests"` → **19 passed, 0 failed**.
- Full suite: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` → 6759 passed; 105 pre-existing failures are Docker-dependent integration tests unrelated to this change (per repo's known CI exclusion of the Integration category), none touch Bank/BankAccountSettings.

## How to verify

```bash
cd /Users/pajgrtondrej/orca/workspaces/Anela.Heblo/worktrees/feature-4061-Arch-Review-Bank-Bankaccountsettings-Accounts-Has
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.GetBankAccountsHandlerTests|FullyQualifiedName~Anela.Heblo.Tests.Features.Bank.ImportBankStatementHandlerTests"
```
Expected: build succeeds, format reports no changes, 19/19 tests pass.

## Notes

- Ruling: the spec-compliance reviewer flagged (⚠️ cannot verify from diff) that the implementer's step-by-step report claimed intermediate counts of "5" and "15" tests where the brief said "4" and "13". I verified directly against the actual test files: `GetBankAccountsHandlerTests.cs` has exactly 4 `[Fact]` methods (matches brief) and `ImportBankStatementHandlerTests.cs` has 15, not 13 (brief's estimate was off). The combined run of 19 tests matches 4 + 15 exactly and all pass. This is a pre-existing inaccuracy in the task brief's test-count estimate, not a defect introduced by the change — no rework needed.
- The `"None"` → `""` wording change in the "Available accounts" segment of the `ArgumentException` message (when zero accounts are configured) is an accepted, intentional side effect per the task brief; `ImportBankStatementHandlerTests`'s fixture always configures two accounts, so this path isn't exercised by any test.
- `BankAccountSettings.cs` has a pre-existing "no trailing newline at end of file" characteristic, unrelated to and untouched by this change (confirmed via diff markers).
- No worktree/branch was created; all 3 commits landed directly on `feature/4061-Arch-Review-Bank-Bankaccountsettings-Accounts-Has`.

## PR Summary

Removes now-impossible null states from `BankAccountSettings.Accounts` by giving it a default empty-list initializer, then simplifies the two handlers (`GetBankAccountsHandler`, `ImportBankStatementHandler`) that previously guarded against that null case. One now-obsolete unit test that manufactured the null state is removed; its intent is still covered by an adjacent test.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Bank/BankAccountSettings.cs` — `Accounts` defaults to `new()`
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankAccounts/GetBankAccountsHandler.cs` — drop `?? new List<...>()` fallback
- `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs` — drop null-conditional and null-check ternary
- `backend/test/Anela.Heblo.Tests/Features/Bank/GetBankAccountsHandlerTests.cs` — delete obsolete null-list test

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01SSpBtnSwW3QRii3BsqKnw4

## Status
DONE
