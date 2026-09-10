# Implementation: remove-hangfire-attribute-from-product-pairing-dqt-job

## What was implemented
Removed the `using Hangfire;` import and the `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]`
attribute from `ProductPairingDqtJob.ExecuteAsync`, so this job no longer references Hangfire directly and now falls
back to Hangfire's default retry policy — matching its three DQT siblings (`InvoiceDqtJob`, `StockWriteBackDqtJob`,
`LotStockReconciliationDqtJob`), none of which reference Hangfire. No other line in the file was touched.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs` — deleted the
  `using Hangfire;` line and the `[AutomaticRetry(...)]` attribute line immediately above `ExecuteAsync`. Net diff:
  2 deletions, 0 additions.

## Tests
- `grep -n "Hangfire" .../ProductPairingDqtJob.cs` — no output (confirmed).
- `grep -rn "Hangfire" backend/src/Anela.Heblo.Application/Features/DataQuality/` — no output (confirmed).
- `dotnet build` (via `dotnet build Anela.Heblo.sln` from repo root, since the `.sln` lives at the repo root, not under
  `backend/`) — succeeded, 0 errors, only pre-existing nullable-reference warnings in unrelated test files.
- `dotnet test --filter "FullyQualifiedName~Features.DataQuality"` — Passed: 90, Failed: 0, Skipped: 0.
- `dotnet test --filter "FullyQualifiedName~Architecture"` — Passed: 35, Failed: 0, Skipped: 0.
- `dotnet format Anela.Heblo.sln --verify-no-changes` — exit code 2, but the only file it wants to reformat is
  `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`, completely unrelated to
  this task. Verified this is **pre-existing** drift, not caused by this change: re-ran the same format-verify command
  with the `ProductPairingDqtJob.cs` edit stashed out, and the identical exit code 2 / identical single-file finding
  reproduced against the unmodified tree. `ProductPairingDqtJob.cs` itself was not among the files needing
  reformatting in either run.

## How to verify
```
cd backend && dotnet build ../Anela.Heblo.sln
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.DataQuality"
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Architecture"
grep -rn "Hangfire" backend/src/Anela.Heblo.Application/Features/DataQuality/
git show HEAD -- backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs
```

## Notes
- The solution file (`Anela.Heblo.sln`) is at the repo root, not under `backend/`, so `dotnet build`/`dotnet format`
  were run against `Anela.Heblo.sln` from the worktree root (equivalent result to running under `backend/` since
  there's no separate solution file there).
- `dotnet format --verify-no-changes` does not exit 0 for the whole solution because of a pre-existing, unrelated
  formatting drift in `GetIssuedInvoiceDetailHandlerTests.cs` (confirmed independent of this change, see Tests
  section above). Per the acceptance criteria ("No file other than `ProductPairingDqtJob.cs` is modified"), I did
  **not** run a blanket `dotnet format` fix, since that would have reformatted this unrelated file and violated the
  file-scope constraint. `ProductPairingDqtJob.cs` itself required no formatting changes.
- `artifacts/feat-3970/state.json` shows as modified in `git status` but was not touched by me and was not part of
  this task's file list — left untouched and not committed, consistent with "commit ONLY the one changed file".
- Committed as `9a42ad0` on the current branch `feature/3970-Arch-Review-Dataquality-Hangfire-Automaticretry-At`. Did
  not push, per instructions.

## Status
DONE
