### task: fix-hangfire-getjobstartedat-full-scan

**Context — what's being fixed and why:**
`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` implements `IBackgroundWorker`. Its `GetJobById(string jobId)` method (lines 126–152) does two O(1) lookups on an `IStorageConnection` — `connection.GetJobData(jobId)` and, via `GetJobState`, `connection.GetStateData(jobId)` — but delegates `StartedAt` resolution to `GetJobStartedAt` (lines 160–177), which instead pages through **every** job in the Hangfire "Processing" state via the monitoring API (`monitoring.ProcessingJobs(0, int.MaxValue)`) and does a linear `FirstOrDefault` scan to find the one entry matching `jobId`. This is unnecessary O(N) storage/memory cost for what should be a single keyed lookup — the exact same `StartedAt` value is already present in the "Processing" state's own `Data` dictionary, retrievable via `connection.GetStateData(jobId)` (the same call `GetJobState` already makes for the same `jobId`).

This task rewrites only the body of `GetJobStartedAt`, preserving its private static signature and its only caller (`GetJobById`) untouched, and adds regression tests for the class (which today has zero coverage of `GetJobById`/`GetJobStartedAt` — only constructor/options wiring is tested).

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` (rewrite `GetJobStartedAt`, lines 160–177)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs` (add regression tests; wire into the shared `[Collection("Hangfire")]` fixture)
- Reference (read-only, no changes): `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/Infrastructure/HangfireTestFixture.cs` — existing `HangfireTestFixture` / `[CollectionDefinition("Hangfire", DisableParallelization = true)]` that configures `JobStorage.Current` to `Hangfire.MemoryStorage.MemoryStorage()` once per test run. Reuse this; do not create a new fixture.

---

- [ ] **Step 1: Write the failing tests in `HangfireBackgroundWorkerTests.cs`**

Replace the entire current file content with the following (it keeps the two existing constructor tests unchanged and adds the new state-based coverage plus the test-seeding helpers):

```csharp
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Anela.Heblo.Xcc;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

[Collection("Hangfire")]
public class HangfireBackgroundWorkerTests
{
    private readonly HangfireBackgroundWorker _worker;

    public HangfireBackgroundWorkerTests(HangfireTestFixture fixture)
    {
        // HangfireTestFixture (shared via the "Hangfire" collection) configures
        // JobStorage.Current to an in-memory Hangfire.MemoryStorage instance once
        // for the whole test run — see HangfireTestFixture.cs.
        _worker = new HangfireBackgroundWorker(Options.Create(new HangfireOptions()));
    }

    [Fact]
    public void Constructor_StoresHangfireOptions()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions { MaxPendingJobsPageSize = 200 });

        // Act
        var worker = new HangfireBackgroundWorker(options);

        // Assert — the worker must hold the options so its monitoring calls use the cap.
        var stored = typeof(HangfireBackgroundWorker)
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker) as HangfireOptions;

        stored.Should().NotBeNull();
        stored!.MaxPendingJobsPageSize.Should().Be(200);
    }

    [Fact]
    public void Constructor_AcceptsCustomPageSize()
    {
        // Arrange
        var options = Options.Create(new HangfireOptions { MaxPendingJobsPageSize = 50 });

        // Act
        var worker = new HangfireBackgroundWorker(options);

        // Assert
        var stored = typeof(HangfireBackgroundWorker)
            .GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(worker) as HangfireOptions;

        stored!.MaxPendingJobsPageSize.Should().Be(50);
    }

    #region GetJobById / GetJobStartedAt state coverage (targeted GetStateData lookup)

    [Fact]
    public void GetJobById_ProcessingStateWithValidStartedAt_ReturnsMatchingDateTime()
    {
        // Arrange: a job whose current Hangfire state is "Processing" and whose
        // state Data dictionary carries a valid, parseable "StartedAt" entry.
        var jobId = CreateEnqueuedJob();
        var expectedStartedAt = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);
        SeedJobState(jobId, ProcessingState.StateName, new Dictionary<string, string>
        {
            ["StartedAt"] = JobHelper.SerializeDateTime(expectedStartedAt)
        });

        // Act
        var result = _worker.GetJobById(jobId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be("Processing");
        result.StartedAt.Should().Be(expectedStartedAt);
    }

    [Fact]
    public void GetJobById_NonProcessingState_ReturnsNullStartedAt()
    {
        // Arrange: job is created directly into the "Enqueued" state and never
        // transitioned to "Processing".
        var jobId = CreateEnqueuedJob();

        // Act
        var result = _worker.GetJobById(jobId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be("Enqueued");
        result.StartedAt.Should().BeNull();
    }

    [Fact]
    public void GetJobById_ProcessingStateWithMissingStartedAtKey_ReturnsNullStartedAt()
    {
        // Arrange: state name is "Processing" but the state Data dictionary has
        // no "StartedAt" entry at all.
        var jobId = CreateEnqueuedJob();
        SeedJobState(jobId, ProcessingState.StateName, new Dictionary<string, string>());

        // Act
        var result = _worker.GetJobById(jobId);

        // Assert
        result.Should().NotBeNull();
        result!.State.Should().Be("Processing");
        result.StartedAt.Should().BeNull();
    }

    [Fact]
    public void GetJobById_NonexistentJobId_ReturnsNull()
    {
        // Act
        var result = _worker.GetJobById("nonexistent-job-id-does-not-exist");

        // Assert
        result.Should().BeNull();
    }

    private static string CreateEnqueuedJob()
    {
        var client = new BackgroundJobClient(JobStorage.Current);
        Expression<Action> methodCall = () => Console.WriteLine("test job");
        var job = Job.FromExpression(methodCall);
        return client.Create(job, new EnqueuedState());
    }

    /// <summary>
    /// Seeds the given job directly into the given state/data via a write transaction,
    /// bypassing Hangfire's normal state-transition pipeline. This is necessary because
    /// Hangfire.States.ProcessingState's constructor is internal and cannot be
    /// instantiated from test code; FakeState below stands in for it, carrying only
    /// the (Name, Data) pair that HangfireBackgroundWorker.GetJobStartedAt reads.
    /// </summary>
    private static void SeedJobState(string jobId, string stateName, Dictionary<string, string> data)
    {
        using var connection = JobStorage.Current.GetConnection();
        using var transaction = connection.CreateWriteTransaction();
        transaction.SetJobState(jobId, new FakeState(stateName, data));
        transaction.Commit();
    }

    private sealed class FakeState : IState
    {
        private readonly Dictionary<string, string> _data;

        public FakeState(string name, Dictionary<string, string> data)
        {
            Name = name;
            _data = data;
        }

        public string Name { get; }
        public string? Reason => null;
        public bool IsFinal => false;
        public bool IgnoreJobLoadException => false;
        public Dictionary<string, string> SerializeData() => _data;
    }

    #endregion
}
```

Notes on this test design (verified against the actual installed `Hangfire.Core 1.8.21` package via a throwaway reflection probe before writing this plan — do not deviate from these facts):
- `Hangfire.Storage.StateData.Data` is `IDictionary<string, string>`, and `Hangfire.Common.JobHelper.DeserializeNullableDateTime(string) : DateTime?` is the correct reader for the `"StartedAt"` value serialized via `JobHelper.SerializeDateTime`.
- `Hangfire.States.ProcessingState` has only an **internal** `(string serverId, string workerId)` constructor — it cannot be `new`'d from test code. `IWriteOnlyTransaction.SetJobState(string jobId, IState state)` accepts any `IState`, so the minimal `FakeState` above (implementing `Name`/`Reason`/`IsFinal`/`IgnoreJobLoadException`/`SerializeData()`) is the correct, dependency-free way to seed a job into an arbitrary named state with arbitrary state data, without needing Hangfire's real state-transition/filter pipeline (which `HangfireBackgroundWorker.GetJobStartedAt` doesn't depend on either — it only reads `connection.GetStateData(jobId)`).
- Confirmed via an end-to-end run against the *actual* `HangfireBackgroundWorker.GetJobById`: with today's (unfixed) `GetJobStartedAt` implementation, `GetJobById_ProcessingStateWithValidStartedAt_ReturnsMatchingDateTime` and `GetJobById_ProcessingStateWithMissingStartedAtKey_ReturnsNullStartedAt`'s "Processing" scenarios do **not** find the seeded value (the old `ProcessingJobs()` monitoring-API scan only sees jobs added to storage's "processing" set by the real state-transition pipeline, which `SetJobState` alone does not populate) — so the first of these two tests is expected to fail before Step 3's fix and pass after it. This is the intended TDD red/green signal.

- [ ] **Step 2: Run tests to verify the new ones fail for the expected reason**

Run:
```bash
cd /home/user/worktrees/feature-3637-Arch-Review-Backgroundjobs-Getjobstartedat-Scans-A/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HangfireBackgroundWorkerTests"
```
Expected: `GetJobById_ProcessingStateWithValidStartedAt_ReturnsMatchingDateTime` FAILS (`result.StartedAt` is `null`, not the expected `DateTime`). The other three new tests (`NonProcessingState`, `MissingStartedAtKey`, `NonexistentJobId`) and the two pre-existing constructor tests PASS already, since they don't depend on the buggy scan path returning a value.

- [ ] **Step 3: Replace `GetJobStartedAt`'s body in `HangfireBackgroundWorker.cs`**

Current code (lines 160–177 of `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs`):

```csharp
    private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)
    {
        try
        {
            var monitoring = JobStorage.Current.GetMonitoringApi();
            var processingJobs = monitoring.ProcessingJobs(0, int.MaxValue);

            var processingJob = processingJobs.FirstOrDefault(j => j.Key == jobId);
            if (processingJob.Value != null)
                return processingJob.Value.StartedAt;

            return null;
        }
        catch
        {
            return null;
        }
    }
```

Replace it with:

```csharp
    private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)
    {
        try
        {
            var stateData = connection.GetStateData(jobId);
            if (stateData?.Name != ProcessingState.StateName)
                return null;

            return stateData.Data.TryGetValue("StartedAt", out var startedAt)
                ? JobHelper.DeserializeNullableDateTime(startedAt)
                : null;
        }
        catch
        {
            return null;
        }
    }
```

No `using` changes are needed: the file already has `using Hangfire.Common;` (for `JobHelper`) and `using Hangfire.States;` (for `ProcessingState`) at the top (lines 5–6), and `IStorageConnection`/`JobStorage` are already imported via `using Hangfire.Storage;` / `using Hangfire;` (lines 7, 4). No other method in the class changes; `GetJobById` (lines 126–152) calls `GetJobStartedAt(connection, jobId)` exactly as before (line 144) and requires no edit.

- [ ] **Step 4: Run tests to verify everything passes**

Run:
```bash
cd /home/user/worktrees/feature-3637-Arch-Review-Backgroundjobs-Getjobstartedat-Scans-A/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~HangfireBackgroundWorkerTests"
```
Expected: all 6 tests PASS (`Constructor_StoresHangfireOptions`, `Constructor_AcceptsCustomPageSize`, `GetJobById_ProcessingStateWithValidStartedAt_ReturnsMatchingDateTime`, `GetJobById_NonProcessingState_ReturnsNullStartedAt`, `GetJobById_ProcessingStateWithMissingStartedAtKey_ReturnsNullStartedAt`, `GetJobById_NonexistentJobId_ReturnsNull`).

- [ ] **Step 5: Full backend validation**

Run, from the repo root:
```bash
cd /home/user/worktrees/feature-3637-Arch-Review-Backgroundjobs-Getjobstartedat-Scans-A/backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test Anela.Heblo.sln
```
Expected: build succeeds with no new warnings/errors introduced by this change; `dotnet format --verify-no-changes` reports no formatting violations (if it reports violations caused by this change's new code, run `dotnet format Anela.Heblo.sln` to fix and re-verify); the full test suite passes, including the 6 tests in `HangfireBackgroundWorkerTests`.

- [ ] **Step 6: Commit**

```bash
cd /home/user/worktrees/feature-3637-Arch-Review-Backgroundjobs-Getjobstartedat-Scans-A
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs backend/test/Anela.Heblo.Tests/Features/Invoices/HangfireBackgroundWorkerTests.cs
git commit -m "perf: replace full ProcessingJobs scan in GetJobStartedAt with targeted GetStateData lookup"
```

**Acceptance criteria for this task (traced to spec.r1.md FR-1/FR-2/FR-3):**
- [ ] `HangfireBackgroundWorker.GetJobStartedAt` contains no call to `ProcessingJobs` or any other `int.MaxValue`-paged monitoring API call (grep confirms: `grep -n "ProcessingJobs" backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs` should only match the bounded call inside `GetRunningJobs`, not `GetJobStartedAt`).
- [ ] A job in `"Processing"` state with a parseable `"StartedAt"` entry yields that exact `DateTime` via `GetJobById`.
- [ ] A job in a non-`"Processing"` state yields `StartedAt == null` via `GetJobById`.
- [ ] A job in `"Processing"` state missing the `"StartedAt"` key yields `StartedAt == null`.
- [ ] A nonexistent job ID yields `GetJobById(...) == null`.
- [ ] `GetJobStartedAt`'s signature (`private static DateTime? GetJobStartedAt(IStorageConnection connection, string jobId)`) and its only caller (`GetJobById`) are unchanged.
- [ ] `IBackgroundWorker`, `BackgroundJobInfo`, `GetPendingJobs`, `GetRunningJobs`, `GetJobState`, `GetJobDisplayName` are unchanged.
- [ ] `dotnet build`, `dotnet format --verify-no-changes`, and the full `dotnet test` suite all pass.
