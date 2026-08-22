# Unit Test Coverage for PlaudPollingJob Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `PlaudPollingJob.ExecuteAsync` (currently 33.9% line coverage) above the 60% coverage threshold by adding a self-contained unit test suite that exercises the job-enabled gate, the `Skipped`/`NotGenerated` counter branching, and per-item exception swallowing — with zero production code changes.

**Architecture:** Add a single new xUnit test file, `PlaudPollingJobTests.cs`, alongside the existing `IngestPlaudRecordingHandlerTests.cs` in `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/`. All five constructor dependencies of `PlaudPollingJob` are mocked with Moq (`IPlaudClient`, `IMediator`, `IRecurringJobStatusChecker`, `ILogger<PlaudPollingJob>`) except `IOptions<MeetingTasksOptions>`, which uses the real `Options.Create(...)` wrapper. Because the job's `ingested`/`skipped`/`notGenerated` counters are local variables with no other externally observable state, the counter branches are pinned by asserting on the rendered `ILogger` summary line via `Mock<ILogger<T>>.Verify(x => x.Log(...))` — the exact 5-argument matcher idiom already used in `LeafletIngestionJobTests.cs`.

**Tech Stack:** .NET 8, xUnit 2.9.2 (`[Fact]`, global `using Xunit;` via `<Using Include="Xunit" />` in the csproj — no explicit `using Xunit;` needed), Moq 4.20.72, FluentAssertions 6.12.0.

---

## File Structure

- **Create:** `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs` — the entire deliverable. `public sealed class PlaudPollingJobTests`, namespace `Anela.Heblo.Tests.Features.MeetingTasks`. Contains a constructor building a fresh `PlaudPollingJob` + fresh mocks (no shared mutable state), and five `[Fact]` test methods, one per scenario in the design doc's test case list.
- **No other files are created or modified.** No `.csproj` change (the test project already references xUnit, Moq, and FluentAssertions, confirmed in `Anela.Heblo.Tests.csproj`). No production code under `backend/src/` is touched (spec FR-6).

## Reference material (read, do not modify)

- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` — class under test.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IPlaudClient.cs` — `Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default)`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudRecordingSummary.cs` — `{ string Id, string Name, DateTime CreatedAt }`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingRequest.cs` — `{ string PlaudRecordingId, string Name, DateTime PlaudCreatedAt }`, implements `IRequest<IngestPlaudRecordingResponse>`.
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingResponse.cs` — `{ bool Skipped, bool NotGenerated, Guid? TranscriptId }`, extends `BaseResponse` (`Success` defaults to `true`).
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs` — `{ int MaxRecordingAgeDays = 7, ... }`.
- `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs` — `Task<bool> IsJobEnabledAsync(string jobName, CancellationToken cancellationToken = default, bool defaultIfMissing = true)`.
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs` — source of the `Mock<ILogger>.Verify(x => x.Log(...))` 5-arg idiom (lines 334–341 of that file) and the `CreateJob()`-style fixture pattern.
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs` — sibling file in the same directory; confirms namespace, `sealed class`, and no explicit `using Xunit;`/`using Moq;` ordering conventions for this directory.

### Exact production log templates (from `PlaudPollingJob.cs`, verified by direct read — do not paraphrase in assertions)

```
Line 47: "Job {JobName} is disabled. Skipping."                                                    (LogInformation, gate-disabled path)
Line 51: "Starting {JobName}"                                                                       (LogInformation)
Line 56: "{Ready} recording(s) found to ingest"                                                     (LogInformation)
Line 89: "Failed to ingest recording {RecordingId}"                                                 (LogError, exception as first arg)
Line 93-95: "{JobName} complete. {Ingested} new recordings ingested, {Skipped} already known, {NotGenerated} not yet generated"   (LogInformation, final summary)
```

Rendered (structured-logging default formatter), for the three counter combinations used in this plan's tests:
- `Ingested=0, Skipped=0, NotGenerated=1` → `"...0 new recordings ingested, 0 already known, 1 not yet generated"`
- `Ingested=0, Skipped=1, NotGenerated=0` → `"...0 new recordings ingested, 1 already known, 0 not yet generated"`
- `Ingested=1, Skipped=0, NotGenerated=0` → `"...1 new recordings ingested, 0 already known, 0 not yet generated"`

### Note on "failing test" steps

This ticket adds coverage for **already-correct, already-implemented** production code (spec FR-6 forbids any change to `PlaudPollingJob.cs`). There is no red/green TDD cycle here — every test in this plan is expected to **PASS on first run** once written correctly. Steps below say "run to verify it passes" rather than "run to verify it fails" for this reason. If any test fails, the default assumption should be a bug in the *test* (wrong mock setup, wrong log-substring, wrong argument), not a bug in `PlaudPollingJob.cs` — per FR-6, a genuine production defect discovered along the way should be flagged, not silently fixed.

---

### task: add-plaudpollingjob-unit-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs`

#### Goal

Add the missing `PlaudPollingJobTests.cs` test file so that `PlaudPollingJob.ExecuteAsync`'s three untested branches — the job-disabled early return (FR-3), the `Skipped`/`NotGenerated` vs. `Skipped`/known-duplicate vs. ingested three-way counter branch (FR-4), and per-item exception swallowing in the polling loop (FR-5) — are all covered, raising line coverage of `PlaudPollingJob.cs` from 33.9% to comfortably above the 60% threshold (NFR-3), with zero production code changes (FR-6).

#### Context

`PlaudPollingJob` has five constructor dependencies, all mocked except the options wrapper:

```csharp
public PlaudPollingJob(
    IPlaudClient plaudClient,
    IMediator mediator,
    IRecurringJobStatusChecker statusChecker,
    IOptions<MeetingTasksOptions> options,
    ILogger<PlaudPollingJob> logger)
```

`Metadata.JobName` is the constant string `"plaud-polling"`. The production gate call is `await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken)` — only two arguments are passed at the call site, so the compiler substitutes the interface's `defaultIfMissing = true` default; the effective call is always `IsJobEnabledAsync("plaud-polling", <token>, true)`. Mock setups must therefore match all three parameters (using `It.IsAny<bool>()`, matching `true`) to be hit.

The full `ExecuteAsync` control flow (already read from source, verbatim):

```csharp
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public async Task ExecuteAsync(CancellationToken cancellationToken = default)
{
    if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
    {
        _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
        return;
    }

    _logger.LogInformation("Starting {JobName}", Metadata.JobName);

    var maxAgeDays = _options.Value.MaxRecordingAgeDays;
    var readyRecordings = await _plaudClient.ListRecentAsync(maxAgeDays, cancellationToken);

    _logger.LogInformation("{Ready} recording(s) found to ingest", readyRecordings.Count);

    int ingested = 0;
    int skipped = 0;
    int notGenerated = 0;

    foreach (var recording in readyRecordings)
    {
        try
        {
            var request = new IngestPlaudRecordingRequest
            {
                PlaudRecordingId = recording.Id,
                Name = recording.Name,
                PlaudCreatedAt = recording.CreatedAt
            };

            var response = await _mediator.Send(request, cancellationToken);

            if (response.Skipped)
            {
                if (response.NotGenerated)
                    notGenerated++;
                else
                    skipped++;
            }
            else
            {
                ingested++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest recording {RecordingId}", recording.Id);
        }
    }

    _logger.LogInformation(
        "{JobName} complete. {Ingested} new recordings ingested, {Skipped} already known, {NotGenerated} not yet generated",
        Metadata.JobName, ingested, skipped, notGenerated);
}
```

The `Mock<ILogger<T>>.Verify(...)` idiom to copy verbatim (from `LeafletIngestionJobTests.cs:334-341`) — the 5-argument `Log(...)` overload matcher. Omitting the trailing `Func<It.IsAnyType, Exception?, string>` matcher makes `Verify` silently never match, so all five arguments must be present in every logger `Verify` call:

```csharp
_mockLogger.Verify(
    x => x.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("...")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```

The test project (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`) has `<Using Include="Xunit" />`, so `[Fact]` and other xUnit types are available without an explicit `using Xunit;` — matching `IngestPlaudRecordingHandlerTests.cs`, which also omits it.

#### Implementation steps

- [ ] **Step 1: Create the test file with usings, fixture fields, and constructor (no test methods yet)**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks;
using Anela.Heblo.Application.Features.MeetingTasks.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class PlaudPollingJobTests
{
    private readonly Mock<IPlaudClient> _mockPlaudClient = new();
    private readonly Mock<IMediator> _mockMediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker = new();
    private readonly Mock<ILogger<PlaudPollingJob>> _mockLogger = new();
    private readonly PlaudPollingJob _job;

    public PlaudPollingJobTests()
    {
        _mockStatusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-polling", It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(true);

        _job = new PlaudPollingJob(
            _mockPlaudClient.Object,
            _mockMediator.Object,
            _mockStatusChecker.Object,
            Options.Create(new MeetingTasksOptions { MaxRecordingAgeDays = 7 }),
            _mockLogger.Object);
    }
}
```

- [ ] **Step 2: Run build to verify the skeleton compiles**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet build backend/test/Anela.Heblo.Tests/`
Expected: Build succeeds, 0 errors. (No tests exist yet, so there is nothing to run with `dotnet test` at this point.)

- [ ] **Step 3: Add the job-disabled gate test (FR-3)**

Insert this method inside the `PlaudPollingJobTests` class body:

```csharp
    [Fact]
    public async Task ExecuteAsync_WhenJobDisabled_SkipsWithoutCallingPlaudOrMediator()
    {
        // Arrange
        _mockStatusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        // Act
        var act = async () => await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        _mockPlaudClient.Verify(
            c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockMediator.Verify(
            m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 4: Run the new test to verify it passes**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PlaudPollingJobTests.ExecuteAsync_WhenJobDisabled_SkipsWithoutCallingPlaudOrMediator"`
Expected: PASS — the overriding `Setup` (broader matcher, registered after the constructor's) wins per Moq's most-recent-matching-setup resolution, `IsJobEnabledAsync` returns `false`, and the method returns before calling `ListRecentAsync` or `Send`.

- [ ] **Step 5: Add the `Skipped && NotGenerated` counter test (FR-4, branch 1)**

Insert this method after the previous one:

```csharp
    [Fact]
    public async Task ExecuteAsync_WhenRecordingSkippedAndNotGenerated_LogsNotGeneratedCount()
    {
        // Arrange
        var recording = new PlaudRecordingSummary
        {
            Id = "rec-1",
            Name = "Weekly sync",
            CreatedAt = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { recording });

        _mockMediator
            .Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = true, NotGenerated = true });

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r =>
                    r.PlaudRecordingId == "rec-1" &&
                    r.Name == "Weekly sync" &&
                    r.PlaudCreatedAt == recording.CreatedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "0 new recordings ingested, 0 already known, 1 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 6: Run the new test to verify it passes**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PlaudPollingJobTests.ExecuteAsync_WhenRecordingSkippedAndNotGenerated_LogsNotGeneratedCount"`
Expected: PASS.

- [ ] **Step 7: Add the `Skipped && !NotGenerated` counter test (FR-4, branch 2)**

Insert this method after the previous one:

```csharp
    [Fact]
    public async Task ExecuteAsync_WhenRecordingSkippedAndAlreadyKnown_LogsSkippedCount()
    {
        // Arrange
        var recording = new PlaudRecordingSummary
        {
            Id = "rec-2",
            Name = "Standup",
            CreatedAt = new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { recording });

        _mockMediator
            .Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = true, NotGenerated = false });

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r =>
                    r.PlaudRecordingId == "rec-2" &&
                    r.Name == "Standup" &&
                    r.PlaudCreatedAt == recording.CreatedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "0 new recordings ingested, 1 already known, 0 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 8: Run the new test to verify it passes**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PlaudPollingJobTests.ExecuteAsync_WhenRecordingSkippedAndAlreadyKnown_LogsSkippedCount"`
Expected: PASS.

- [ ] **Step 9: Add the ingested (non-skipped) contrast test (FR-4, branch 3)**

Insert this method after the previous one:

```csharp
    [Fact]
    public async Task ExecuteAsync_WhenRecordingIngested_LogsIngestedCount()
    {
        // Arrange
        var recording = new PlaudRecordingSummary
        {
            Id = "rec-3",
            Name = "Planning",
            CreatedAt = new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { recording });

        _mockMediator
            .Setup(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false });

        // Act
        await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r =>
                    r.PlaudRecordingId == "rec-3" &&
                    r.Name == "Planning" &&
                    r.PlaudCreatedAt == recording.CreatedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "1 new recordings ingested, 0 already known, 0 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 10: Run the new test to verify it passes**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PlaudPollingJobTests.ExecuteAsync_WhenRecordingIngested_LogsIngestedCount"`
Expected: PASS.

- [ ] **Step 11: Add the per-item exception-swallowing test (FR-5)**

Insert this method after the previous one, immediately before the class's closing brace:

```csharp
    [Fact]
    public async Task ExecuteAsync_WhenMediatorThrowsForOneRecording_ContinuesProcessingRemainingRecordings()
    {
        // Arrange
        var failingRecording = new PlaudRecordingSummary
        {
            Id = "rec-fail",
            Name = "Broken meeting",
            CreatedAt = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc)
        };
        var survivingRecording = new PlaudRecordingSummary
        {
            Id = "rec-ok",
            Name = "Good meeting",
            CreatedAt = new DateTime(2026, 8, 14, 9, 0, 0, DateTimeKind.Utc)
        };

        _mockPlaudClient
            .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaudRecordingSummary> { failingRecording, survivingRecording });

        var thrownException = new InvalidOperationException("Mediator pipeline failure");

        _mockMediator
            .Setup(m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-fail"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(thrownException);

        _mockMediator
            .Setup(m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-ok"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestPlaudRecordingResponse { Skipped = false });

        // Act
        var act = async () => await _job.ExecuteAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-fail"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockMediator.Verify(
            m => m.Send(
                It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-ok"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to ingest recording rec-fail")),
                It.Is<Exception>(ex => ex == thrownException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
                    "1 new recordings ingested, 0 already known, 0 not yet generated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

- [ ] **Step 12: Run the new test to verify it passes**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PlaudPollingJobTests.ExecuteAsync_WhenMediatorThrowsForOneRecording_ContinuesProcessingRemainingRecordings"`
Expected: PASS — the `catch (Exception ex)` block logs the error and the loop continues to the second recording; the summary log reflects only the surviving recording (`Ingested=1`), with the failed one excluded from all three counters.

- [ ] **Step 13: Run the full `PlaudPollingJobTests` class**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~PlaudPollingJobTests"`
Expected: PASS — all 5 tests pass, 0 failed.

- [ ] **Step 14: Run the full `Anela.Heblo.Tests` project to confirm no regressions**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && dotnet build backend/test/Anela.Heblo.Tests/ && dotnet test backend/test/Anela.Heblo.Tests/`
Expected: Build succeeds with 0 errors; full test suite passes (0 failed). If any pre-existing unrelated test was already failing before this change, confirm via `git stash` + rerun that it is not a regression introduced by this file before proceeding.

- [ ] **Step 15: Run `dotnet format` per repo convention**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item/backend && dotnet format`
Expected: No formatting violations reported (or auto-fixed cleanly); re-run Step 14 if `dotnet format` changed anything.

- [ ] **Step 16: Confirm the diff touches only the new test file**
Run: `cd /home/user/worktrees/feature-3936-Coverage-Gap-Meetingtasks-Plaudpollingjob-Per-Item && git status --porcelain`
Expected: Only `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs` shown as a new/untracked file; no changes under `backend/src/` (confirms FR-6/spec acceptance criterion).

- [ ] **Step 17: Commit**
```bash
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs
git commit -m "test: add PlaudPollingJob unit test coverage for gate, counter branching, and per-item exception swallowing"
```
