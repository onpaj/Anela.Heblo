# Fix Running Invoice Import Jobs Filter Implementation Plan

**Goal:** Fix `GetRunningInvoiceImportJobsHandler`'s job-name filter so it actually matches the real Hangfire display name (`"Import faktur: {description}"`) instead of the never-occurring substring `"InvoiceImport"`, so the running-jobs endpoint stops silently returning an empty list.
**Architecture:** Single-file, single-predicate bug fix fully contained in the Invoices module's vertical slice — `GetRunningInvoiceImportJobsHandler.Handle` changes its `.Where(...)` filter from a broken `Contains("InvoiceImport", ...)` check to a `StartsWith("Import faktur:", ...)` check that matches what `HangfireBackgroundWorker.GetJobDisplayName` actually produces for jobs backed by `InvoiceImportService.ImportInvoicesAsync` (which carries `[DisplayName("Import faktur: {0}")]`). No other component, contract, or DTO changes.
**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions.

---

### task: fix-running-invoice-import-jobs-filter

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs:50-54`
- Test: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs`

**Context:** `GetRunningInvoiceImportJobsHandler.Handle` currently filters running/pending Hangfire jobs with:

```csharp
// Filter for invoice import jobs based on job name containing "InvoiceImport"
var invoiceImportJobs = runningJobs
    .Concat(pendingJobs)
    .Where(job => job.JobName != null &&
                  job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase))
    .ToList();
```

In production, invoice-import Hangfire jobs are always launched via `InvoiceImportService.ImportInvoicesAsync`, which carries `[DisplayName("Import faktur: {0}")]`. `HangfireBackgroundWorker.GetJobDisplayName` resolves the job's `JobName` to this attribute text with `{0}` substituted by the description (e.g. `"Import faktur: faktura 12345"`, `"Import faktur: denní import CZK za 12.07.2026"`). That string never contains the substring `"InvoiceImport"`, so the filter always evaluates to false and the handler always returns `[]`, even while an import is actively running or queued. The comment on the line above the filter is also stale — it describes the old, incorrect "contains InvoiceImport" logic and must be updated to describe the new predicate.

The existing test file mocks `JobName` as `"InvoiceImportJob.Run"` in five places (lines 44, 49, 70, 95, 118), which happens to satisfy the broken `Contains("InvoiceImport", ...)` filter — this is why the bug shipped with all-green tests. This task rewrites those mocked job names to the real production format and adds a regression test proving the old string no longer matches.

Work TDD-style: first update the test file so it encodes the correct (currently failing) expectations, run it to confirm the existing filter fails those expectations, then fix the handler, then confirm all tests pass.

- [ ] **Step 1: Update `Handle_FiltersToInvoiceImportJobsOnly` to use realistic job names and add a regression case for the old broken string**

  Open `backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs`. Replace the entire `Handle_FiltersToInvoiceImportJobsOnly` test method (currently lines 37-61) with:

  ```csharp
    [Fact]
    public async Task Handle_FiltersToInvoiceImportJobsOnly()
    {
        // Arrange
        var worker = new Mock<IBackgroundWorker>();
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("Import faktur: faktura 12345", id: "r1"),
            Job("Daily Invoice DQT Check", id: "r2"),
        });
        worker.Setup(w => w.GetPendingJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("Import faktur: denní import CZK za 12.07.2026", state: "Enqueued", id: "p1"),
            Job("MetaAds Invoice Import", state: "Enqueued", id: "p2"),
            Job("InvoiceImportJob.Run", state: "Enqueued", id: "p3"),
        });

        var handler = CreateHandler(worker.Object, NewCache());

        // Act
        var result = await handler.Handle(new GetRunningInvoiceImportJobsRequest(), CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(j => j.Id).Should().BeEquivalentTo(new[] { "r1", "p1" });
    }
  ```

  This deliberately includes `"Daily Invoice DQT Check"`, `"MetaAds Invoice Import"` (both contain "Invoice" and/or "Import" as loose substrings but must still be excluded, proving the fix isn't just re-matching a different loose substring), and `"InvoiceImportJob.Run"` (the old broken-filter-only string, which does *not* start with `"Import faktur:"` and must now be correctly excluded — this is the FR-2 regression guard).

- [ ] **Step 2: Update the remaining four tests' mocked job names from `"InvoiceImportJob.Run"` to `"Import faktur: ..."`**

  In the same file, in `Handle_CacheHit_DoesNotCallWorkerSecondTime` (currently lines 63-86), replace:

  ```csharp
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
  ```

  with:

  ```csharp
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("Import faktur: faktura 12345", id: "r1"),
        });
  ```

  In `Handle_CacheDisabled_CallsWorkerOnEveryInvocation` (currently lines 88-109), replace:

  ```csharp
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
  ```

  with:

  ```csharp
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("Import faktur: faktura 12345", id: "r1"),
        });
  ```

  In `Handle_CacheDisabled_DoesNotWriteToCache` (currently lines 111-130), replace:

  ```csharp
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("InvoiceImportJob.Run", id: "r1"),
        });
  ```

  with:

  ```csharp
        worker.Setup(w => w.GetRunningJobs()).Returns(new List<BackgroundJobInfo>
        {
            Job("Import faktur: faktura 12345", id: "r1"),
        });
  ```

  `Handle_WorkerThrows_ReturnsEmptyListAndDoesNotCache` (currently lines 132-148) does not mock any job names (it throws before filtering runs) — leave it unchanged.

- [ ] **Step 3: Run the test suite and confirm it fails against the still-broken handler**

  ```bash
  cd /home/user/worktrees/feature-3611-Arch-Review-Invoices-Getrunninginvoiceimportjobsha/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetRunningInvoiceImportJobsHandlerTests"
  ```

  Expected: `Handle_FiltersToInvoiceImportJobsOnly`, `Handle_CacheHit_DoesNotCallWorkerSecondTime`, `Handle_CacheDisabled_CallsWorkerOnEveryInvocation`, and `Handle_CacheDisabled_DoesNotWriteToCache` FAIL (the handler's `Contains("InvoiceImport", ...)` predicate does not match `"Import faktur: ..."` job names, so results come back empty/mismatched). `Handle_WorkerThrows_ReturnsEmptyListAndDoesNotCache` still passes (unaffected by the predicate). This confirms the tests correctly exercise the bug before the fix.

- [ ] **Step 4: Fix the predicate and stale comment in the handler**

  Open `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs`. Replace lines 50-55:

  ```csharp
            // Filter for invoice import jobs based on job name containing "InvoiceImport"
            var invoiceImportJobs = runningJobs
                .Concat(pendingJobs)
                .Where(job => job.JobName != null &&
                              job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase))
                .ToList();
  ```

  with:

  ```csharp
            // Filter for invoice import jobs based on the "Import faktur: {0}" DisplayName
            // produced by InvoiceImportService.ImportInvoicesAsync (via HangfireBackgroundWorker.GetJobDisplayName).
            // NOTE: keep this prefix in sync with the [DisplayName] attribute text if it ever changes.
            var invoiceImportJobs = runningJobs
                .Concat(pendingJobs)
                .Where(job => job.JobName != null &&
                              job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase))
                .ToList();
  ```

  No other lines in this file change. Caching, error handling (catch-and-return-empty-list), and logging remain exactly as they are.

- [ ] **Step 5: Run the test suite again and confirm all tests pass**

  ```bash
  cd /home/user/worktrees/feature-3611-Arch-Review-Invoices-Getrunninginvoiceimportjobsha/backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetRunningInvoiceImportJobsHandlerTests"
  ```

  Expected: all 5 tests pass (`Handle_FiltersToInvoiceImportJobsOnly`, `Handle_CacheHit_DoesNotCallWorkerSecondTime`, `Handle_CacheDisabled_CallsWorkerOnEveryInvocation`, `Handle_CacheDisabled_DoesNotWriteToCache`, `Handle_WorkerThrows_ReturnsEmptyListAndDoesNotCache`).

- [ ] **Step 6: Run the full backend build and test suite to confirm no regressions elsewhere**

  ```bash
  cd /home/user/worktrees/feature-3611-Arch-Review-Invoices-Getrunninginvoiceimportjobsha/backend
  dotnet build
  dotnet format --verify-no-changes
  dotnet test
  ```

  Expected: build succeeds with no errors, `dotnet format --verify-no-changes` reports no formatting violations, and the full test run passes (no failures introduced outside the touched test file).

- [ ] **Step 7: Commit**

  ```bash
  cd /home/user/worktrees/feature-3611-Arch-Review-Invoices-Getrunninginvoiceimportjobsha
  git add backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs
  git commit -m "fix: match real Hangfire display name in running invoice import jobs filter

Predicate previously checked for JobName.Contains(\"InvoiceImport\"), which never
matches the real production display name \"Import faktur: {description}\" set via
[DisplayName] on InvoiceImportService.ImportInvoicesAsync, so the endpoint always
returned an empty list. Changed to StartsWith(\"Import faktur:\", OrdinalIgnoreCase)
and updated the stale comment. Updated unit tests to use realistic job names and
added a regression case for the old, now-excluded \"InvoiceImportJob.Run\" string."
  ```

  Expected: commit succeeds; `git status` shows a clean working tree for these two files.
