# Implementation: add-automatic-retry-attribute

## What was implemented

Added `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` to the `ExecuteAsync` method of `ProductPairingDqtJob`. This tells Hangfire not to retry the job on failure — Polly resilience pipelines inside the job already handle retries and backoff, so allowing Hangfire to also retry would amplify noise and cause duplicate execution attempts.

Added `using Hangfire;` to the file's using directives. `Hangfire.Core` was already listed as a `<PackageReference>` in the Application project's `.csproj`, so no package changes were needed.

## Files created/modified

- **Modified:** `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`
  - Added `using Hangfire;`
  - Added `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` above `ExecuteAsync`

## How to verify

1. Build passes: `dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj -v q` — confirmed 0 errors.
2. At runtime, when `ProductPairingDqtJob.ExecuteAsync` throws an unhandled exception, Hangfire will immediately mark the job as Failed rather than enqueuing retry attempts. Polly's internal retry/backoff pipeline remains the sole retry mechanism.

## Notes

- `Hangfire.Core` 1.8.21 was already a direct `<PackageReference>` in the Application project — no `.csproj` changes were required.
- The 139 compiler warnings in the build output are all pre-existing; none are related to this change.

## Status
DONE
