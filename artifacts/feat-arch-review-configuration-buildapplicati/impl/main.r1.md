---

# Implementation: Remove Speculative Async from BuildApplicationConfigurationAsync

## What was implemented

Converted `GetConfigurationHandler.BuildApplicationConfigurationAsync()` from a fake-async method (which ended with a `await Task.CompletedTask` placeholder) to a proper synchronous `BuildApplicationConfiguration()`. The single in-file call site in `Handle()` was updated to drop the `await`. The MediatR `Handle()` signature is unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — removed `async`/`Task<...>` from private helper, removed `await Task.CompletedTask` placeholder line, removed `Async` suffix from helper name, updated call site on line 32

## Tests

No test files modified. Existing HTTP-level tests in `GetConfigurationEndpointTests.cs` cover the handler's external behavior and do not reference the private helper by name. The Application project builds cleanly (0 errors, 0 new warnings introduced by this change).

**Note:** There is a pre-existing build error in `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` (`IIssuedInvoiceRepository` not found) that prevents running `dotnet test` on the full solution. This error exists on the branch before this commit and is unrelated to the Configuration feature.

## How to verify

```bash
# Confirm Application project builds clean
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj

# Confirm no old async references remain
grep -rn "BuildApplicationConfigurationAsync" backend  # expect no output
grep -nE "await|Task\.CompletedTask" backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs  # expect no output

# Confirm Handle signature intact
grep -n "public async Task<GetConfigurationResponse> Handle" backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs
```

## Notes

The full solution has a pre-existing `CS0246` build error in the Invoices test project unrelated to this PR. The Application layer itself builds with 0 errors and 0 new warnings.

## PR Summary

Remove speculative async overhead from `GetConfigurationHandler` — the private helper `BuildApplicationConfigurationAsync` performed no I/O and terminated with `await Task.CompletedTask` as a placeholder for hypothetical future async work. This violates YAGNI (per project coding standards) and incurs a real per-request async state machine allocation on every `/configuration` call with no current benefit. The fix is a two-line change: convert the helper to `BuildApplicationConfiguration()` returning `ApplicationConfiguration` synchronously, and drop the `await` at the single call site in `Handle()`. The public MediatR `Handle` signature is unchanged.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — helper signature, call site, and removed `await Task.CompletedTask` placeholder

## Status
DONE_WITH_CONCERNS

**Concern:** Pre-existing build error in `Anela.Heblo.Tests.csproj` (`GetIssuedInvoiceDetailHandlerTests.cs` — `IIssuedInvoiceRepository` not found) blocks `dotnet test` from running for the full solution. This is unrelated to this PR but means the endpoint tests could not be run in the automated pipeline. The Application project itself builds cleanly.