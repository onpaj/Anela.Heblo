### task: remove-hangfire-attribute-from-product-pairing-dqt-job

## Goal
Remove `using Hangfire;` and `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` from `ProductPairingDqtJob.ExecuteAsync`, with no other change to the file, and confirm the build/tests are unaffected.

## Files to change

**Edit:**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`

**Verify only, no change expected:**
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtJobTests.cs` — does not reference Hangfire or assert on the attribute; must pass unmodified.
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs` — registration is attribute-agnostic; confirm no edit is needed.
- `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — the `Hangfire` package reference stays; five other job classes elsewhere in this project still use `[AutomaticRetry]` directly, so the reference must not be removed.

**Do not touch:**
- `InvoiceDqtJob.cs`, `StockWriteBackDqtJob.cs`, `LotStockReconciliationDqtJob.cs` — already have no Hangfire references; out of scope.
- `PlaudPollingJob.cs`, `MindMapUpdateJob.cs`, `BreakInsertionJob.cs`, `ProductExportDownloadJob.cs`, `GenerateArticleJob.cs` — same `[AutomaticRetry]`-on-Application-layer-class pattern used deliberately in other modules; explicitly out of scope (see `arch-review.r1.md` Decision 1).

## Steps

- [ ] **Step 1: Confirm current file contents before editing**

Read the current file to confirm line numbers match what this plan assumes:

Run: `sed -n '1,10p;38,42p' backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`

Expected: line 4 is `using Hangfire;` and line 40 is `    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` immediately above `public async Task ExecuteAsync(...)`. If the file has drifted from this (different line numbers, different attribute text), re-read the whole file and adjust the edits below to match reality rather than applying them blindly.

- [ ] **Step 2: Remove the Hangfire using directive**

Current top of file:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;
```

Change to:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs;
```

(Delete the `using Hangfire;` line only — the remaining three `using` lines and the namespace declaration are unchanged, matching the exact `using` block already present in `InvoiceDqtJob.cs`, `StockWriteBackDqtJob.cs`, and `LotStockReconciliationDqtJob.cs`.)

- [ ] **Step 3: Remove the AutomaticRetry attribute**

Current:

```csharp
    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
```

Change to:

```csharp
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
```

(Delete the attribute line only. The method signature, body, and everything else in the class — `Metadata`, constructor, all field assignments — are untouched.)

- [ ] **Step 4: Confirm no other Hangfire reference remains in the file**

Run: `grep -n "Hangfire" backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`
Expected: no output (no matches).

- [ ] **Step 5: Confirm module-wide consistency (FR-2)**

Run: `grep -rn "Hangfire" backend/src/Anela.Heblo.Application/Features/DataQuality/`
Expected: no output (no matches) — all four DQT jobs now have zero Hangfire references.

- [ ] **Step 6: Build the backend**

Run: `cd backend && dotnet build`
Expected: build succeeds with no errors (in particular no `CS0246`-style unresolved-type error where the attribute used to be, and no unused-`using` warning turned error).

- [ ] **Step 7: Run the DataQuality test suite**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~Features.DataQuality"`
Expected: all tests pass, including all four tests in `ProductPairingDqtJobTests` (`ExecuteAsync_JobEnabled_PersistsRunBeforeInvokingRunner`, `ExecuteAsync_JobDisabled_DoesNotPersistOrInvokeRunner`, `ExecuteAsync_PropagatesCancellationTokenToSaveChanges`, `ExecuteAsync_UsesTimeProviderForDateWindow_NotWallClock`) unmodified and green.

- [ ] **Step 8: Run the architecture-fitness test suite**

Run: `cd backend && dotnet test --filter "FullyQualifiedName~Architecture"`
Expected: `ModuleBoundariesTests` and any other architecture-fitness tests pass — this change removes a namespace reference, which cannot newly trip a forbidden-namespace check; confirms no unrelated regression.

- [ ] **Step 9: Run dotnet format**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting changes needed. If it reports changes, run `dotnet format` (without `--verify-no-changes`) and re-stage.

- [ ] **Step 10: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs
git commit -m "fix(dataquality): remove Hangfire AutomaticRetry leak from ProductPairingDqtJob

Removes the using Hangfire; import and [AutomaticRetry] attribute from
ProductPairingDqtJob so it matches its three DQT siblings
(InvoiceDqtJob, StockWriteBackDqtJob, LotStockReconciliationDqtJob),
none of which reference Hangfire. The job now falls back to Hangfire's
default retry policy, same as its siblings already run under.

Fixes #3970"
```

## Acceptance criteria
- `ProductPairingDqtJob.cs` contains no `using Hangfire;` and no `[AutomaticRetry(...)]` attribute (FR-1).
- `grep -rn "Hangfire" backend/src/Anela.Heblo.Application/Features/DataQuality/` returns no matches (FR-2).
- All four existing `ProductPairingDqtJobTests` tests pass unmodified (FR-3).
- `dotnet build` succeeds for the whole backend solution with no errors.
- `dotnet format --verify-no-changes` reports no changes needed.
- No file other than `ProductPairingDqtJob.cs` is modified.
