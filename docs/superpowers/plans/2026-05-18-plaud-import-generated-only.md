# Plaud Import: Filter to Generated Recordings + Per-Meeting Reimport

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Only ingest Plaud recordings that have both `transcript` and `summary` generated on the Plaud side, and add a per-meeting Reimport button on the detail page to re-pull summary + transcript for an already-imported meeting.

**Architecture:** A new `PlaudFileDetail` record + `GetFileDetailAsync` method on `IPlaudClient` lets the ingest handler check generation status before pulling content. A new `ReimportMeetingTranscript` use case exposes the same capability on-demand, wired to a POST endpoint and a React button that invalidates the detail query on success.

**Tech Stack:** .NET 8 / C#, MediatR CQRS, EF Core (tracked entity mutation), React 18, TanStack Query (React Query), Lucide icons, xUnit + FluentAssertions + Moq.

---

## File Structure

### New files
| Path | Purpose |
|------|---------|
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudFileDetail.cs` | Record holding availability flags + `IsGenerated` computed property |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptRequest.cs` | MediatR request |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptResponse.cs` | MediatR response |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs` | Handler: load, check generated, re-fetch, save |
| `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_generated_sample.txt` | Fixture: all available |
| `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_raw_sample.txt` | Fixture: all unavailable |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs` | Handler tests for reimport |

### Modified files
| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IPlaudClient.cs` | Add `GetFileDetailAsync` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` | Implement `GetFileDetailAsync` + `ParseFileDetail` |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingResponse.cs` | Add `NotGenerated` flag |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs` | Gate on `IsGenerated` between idempotency check and transcript fetch |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` | Count + log `notGenerated` responses |
| `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs` | Add `POST {transcriptId}/reimport` endpoint |
| `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj` | Register new fixture files |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs` | Add `GetFileDetailAsync` mocks to existing tests + new "not generated" test |
| `frontend/src/api/hooks/useMeetingTasks.ts` | Add `ReimportMeetingResponse` type + `useReimportMeeting` mutation hook |
| `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` | Add Reimport button + error display |

---

## Task 1: Plaud file-detail parser – fixture files + failing tests

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_generated_sample.txt`
- Create: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_raw_sample.txt`
- Modify: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs`

- [ ] **Step 1: Create the fixture for a generated recording**

Create `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_generated_sample.txt`:
```
- Fetching file...
File Details:
  audio:        available
  transcript:   available
  summary:      available
```

- [ ] **Step 2: Create the fixture for a raw (not-yet-generated) recording**

Create `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_raw_sample.txt`:
```
- Fetching file...
File Details:
  audio:        unavailable
  transcript:   unavailable
  summary:      unavailable
```

- [ ] **Step 3: Register new fixtures in the test project**

In `backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj`, add inside the existing `<ItemGroup>` that already has `plaud_recent_sample.txt`:

```xml
<Content Include="Fixtures\plaud_file_generated_sample.txt">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
<Content Include="Fixtures\plaud_file_raw_sample.txt">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

- [ ] **Step 4: Add failing ParseFileDetail tests**

Append to the bottom of `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs` (before the closing `}`):

```csharp
    [Fact]
    public async Task ParseFileDetail_WithGeneratedFixture_ReturnsIsGeneratedTrue()
    {
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(PlaudCliClientParserTests).Assembly.Location)!,
            "Fixtures", "plaud_file_generated_sample.txt");

        var fixtureContent = await File.ReadAllTextAsync(fixturePath);

        var result = PlaudCliClient.ParseFileDetail(fixtureContent);

        result.TranscriptAvailable.Should().BeTrue();
        result.SummaryAvailable.Should().BeTrue();
        result.AudioAvailable.Should().BeTrue();
        result.IsGenerated.Should().BeTrue();
    }

    [Fact]
    public async Task ParseFileDetail_WithRawFixture_ReturnsIsGeneratedFalse()
    {
        var fixturePath = Path.Combine(
            Path.GetDirectoryName(typeof(PlaudCliClientParserTests).Assembly.Location)!,
            "Fixtures", "plaud_file_raw_sample.txt");

        var fixtureContent = await File.ReadAllTextAsync(fixturePath);

        var result = PlaudCliClient.ParseFileDetail(fixtureContent);

        result.TranscriptAvailable.Should().BeFalse();
        result.SummaryAvailable.Should().BeFalse();
        result.AudioAvailable.Should().BeFalse();
        result.IsGenerated.Should().BeFalse();
    }

    [Fact]
    public void ParseFileDetail_IgnoresHeaderLines()
    {
        const string input = """
            - Fetching file...
            File Details:
              audio:        available
              transcript:   available
              summary:      unavailable
            """;

        var result = PlaudCliClient.ParseFileDetail(input);

        result.AudioAvailable.Should().BeTrue();
        result.TranscriptAvailable.Should().BeTrue();
        result.SummaryAvailable.Should().BeFalse();
        result.IsGenerated.Should().BeFalse();
    }
```

- [ ] **Step 5: Run tests to confirm they FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/ --filter "ParseFileDetail" --no-build 2>&1 | tail -20
```

Expected: build error — `PlaudCliClient.ParseFileDetail` does not exist yet. If it compiles somehow and runs, expect test failures.

---

## Task 2: PlaudFileDetail type + IPlaudClient + ParseFileDetail implementation

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudFileDetail.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IPlaudClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`

- [ ] **Step 1: Create PlaudFileDetail record**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudFileDetail.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed record PlaudFileDetail
{
    public bool TranscriptAvailable { get; init; }
    public bool SummaryAvailable { get; init; }
    public bool AudioAvailable { get; init; }
    public bool IsGenerated => TranscriptAvailable && SummaryAvailable;
}
```

- [ ] **Step 2: Add GetFileDetailAsync to IPlaudClient**

Replace the entire content of `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IPlaudClient.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public interface IPlaudClient
{
    Task<List<PlaudRecordingSummary>> ListRecentAsync(int days, CancellationToken ct = default);
    Task<string> GetTranscriptAsync(string recordingId, CancellationToken ct = default);
    Task<PlaudSummaryResult> GetSummaryAsync(string recordingId, CancellationToken ct = default);
    Task<PlaudFileDetail> GetFileDetailAsync(string recordingId, CancellationToken ct = default);
}
```

- [ ] **Step 3: Add GetFileDetailAsync + ParseFileDetail to PlaudCliClient**

In `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`, add the following two methods. Place them before the closing `}` of the class (after `ParseFilesOutput`):

```csharp
    public async Task<PlaudFileDetail> GetFileDetailAsync(string recordingId, CancellationToken ct = default)
    {
        var output = await RunCliAsync(new[] { "file", recordingId }, ct);
        return ParseFileDetail(output);
    }

    public static PlaudFileDetail ParseFileDetail(string output)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0) continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            if (!string.IsNullOrEmpty(key) && !key.StartsWith('-') && !key.StartsWith("File"))
                lookup[key] = value;
        }

        static bool IsAvailable(Dictionary<string, string> d, string k) =>
            d.TryGetValue(k, out var v) && v.Equals("available", StringComparison.OrdinalIgnoreCase);

        return new PlaudFileDetail
        {
            TranscriptAvailable = IsAvailable(lookup, "transcript"),
            SummaryAvailable    = IsAvailable(lookup, "summary"),
            AudioAvailable      = IsAvailable(lookup, "audio")
        };
    }
```

- [ ] **Step 4: Build the solution**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet build Anela.Heblo.sln 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 5: Run the ParseFileDetail tests to confirm they PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/ --filter "ParseFileDetail" 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Run the full Adapters.Plaud.Tests suite to check for regressions**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/ 2>&1 | tail -10
```

Expected: all passing.

- [ ] **Step 7: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudFileDetail.cs backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IPlaudClient.cs backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_generated_sample.txt backend/test/Anela.Heblo.Adapters.Plaud.Tests/Fixtures/plaud_file_raw_sample.txt backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs && git commit -m "feat: add PlaudFileDetail + GetFileDetailAsync with ParseFileDetail"
```

---

## Task 3: Ingest generation gate – failing tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`

The handler will soon call `GetFileDetailAsync` for every new recording. Three existing tests go through the "new recording" path and will fail once we add the gate (mock won't return anything for `GetFileDetailAsync`, causing a `NullReferenceException` or hanging). Updating them now (before changing the handler) keeps the diff clear and ensures each change can be verified independently.

- [ ] **Step 1: Update existing tests to mock GetFileDetailAsync (generated = true)**

In `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`:

In test `Handle_WithNewRecording_CreatesTranscriptAndTasksInPendingReviewState`, add the following setup block **after** the `ExistsByPlaudIdAsync` setup and before the `GetTranscriptAsync` setup:

```csharp
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
```

In test `Handle_WhenExtractorReturnsEmptyList_SavesTranscriptWithZeroTasks`, add after the `ExistsByPlaudIdAsync` setup:

```csharp
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
```

In test `Handle_WhenLlmReturnsNameWithoutEmail_FillsEmailFromDirectory`, add after the `ExistsByPlaudIdAsync` setup:

```csharp
        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
```

- [ ] **Step 2: Add the "not generated" test**

Add this new test method to the class (before the closing `}`):

```csharp
    [Fact]
    public async Task Handle_WhenRecordingNotGenerated_SkipsWithoutSavingOrFetchingTranscript()
    {
        // Arrange
        var recordingId = "rec_raw";

        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = recordingId,
            Name = "Raw Recording",
            PlaudCreatedAt = DateTime.UtcNow
        };

        _mockRepository
            .Setup(r => r.ExistsByPlaudIdAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync(recordingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = false, SummaryAvailable = false, AudioAvailable = false });

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Skipped.Should().BeTrue();
        response.NotGenerated.Should().BeTrue();

        _mockPlaudClient.Verify(
            c => c.GetTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _mockRepository.Verify(
            r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 3: Run IngestPlaudRecording tests to confirm they FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Tests/ --filter "IngestPlaudRecordingHandlerTests" 2>&1 | tail -20
```

Expected: build error or test failure — `IngestPlaudRecordingResponse` has no `NotGenerated` property yet, and the handler does not call `GetFileDetailAsync`.

---

## Task 4: Add NotGenerated flag to response + gate the handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`

- [ ] **Step 1: Add NotGenerated to IngestPlaudRecordingResponse**

Replace the entire content of `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.IngestPlaudRecording;

public class IngestPlaudRecordingResponse : BaseResponse
{
    public bool Skipped { get; set; }
    public bool NotGenerated { get; set; }
    public Guid? TranscriptId { get; set; }
}
```

- [ ] **Step 2: Add generation gate to IngestPlaudRecordingHandler**

In `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`, insert the following block **after** the existing idempotency check (after `return new IngestPlaudRecordingResponse { Skipped = true };`) and **before** the transcript fetch:

```csharp
        // Check if Plaud has finished generating transcript + summary for this recording
        var detail = await _plaudClient.GetFileDetailAsync(request.PlaudRecordingId, cancellationToken);
        if (!detail.IsGenerated)
        {
            _logger.LogInformation("Recording {RecordingId} not yet generated on Plaud, skipping", request.PlaudRecordingId);
            return new IngestPlaudRecordingResponse { Skipped = true, NotGenerated = true };
        }
```

The relevant section of the handler should now look like:

```csharp
        // Check if recording already exists (idempotency)
        var exists = await _repository.ExistsByPlaudIdAsync(request.PlaudRecordingId, cancellationToken);
        if (exists)
        {
            _logger.LogDebug("Recording {RecordingId} already ingested, skipping", request.PlaudRecordingId);
            return new IngestPlaudRecordingResponse { Skipped = true };
        }

        // Check if Plaud has finished generating transcript + summary for this recording
        var detail = await _plaudClient.GetFileDetailAsync(request.PlaudRecordingId, cancellationToken);
        if (!detail.IsGenerated)
        {
            _logger.LogInformation("Recording {RecordingId} not yet generated on Plaud, skipping", request.PlaudRecordingId);
            return new IngestPlaudRecordingResponse { Skipped = true, NotGenerated = true };
        }

        // Fetch transcript and summary from Plaud
        var transcript = await _plaudClient.GetTranscriptAsync(request.PlaudRecordingId, cancellationToken);
```

- [ ] **Step 3: Run IngestPlaudRecording tests to confirm they PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Tests/ --filter "IngestPlaudRecordingHandlerTests" 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 4: Build the full solution**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet build Anela.Heblo.sln 2>&1 | tail -10
```

Expected: `Build succeeded.`

---

## Task 5: Update PlaudPollingJob to log not-generated count + commit ingest changes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`

- [ ] **Step 1: Add notGenerated counter to PlaudPollingJob**

Replace the `ExecuteAsync` method body in `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`. The new method:

```csharp
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

- [ ] **Step 2: Build to confirm no errors**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet build Anela.Heblo.sln 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 3: Run full Anela.Heblo.Tests suite to check for regressions**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Tests/ 2>&1 | tail -10
```

Expected: all passing (no new failures).

- [ ] **Step 4: Commit ingest changes**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && git add \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingResponse.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs && \
  git commit -m "feat: gate plaud ingest on generation status, log not-generated count"
```

---

## Task 6: Reimport use case – failing tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs`

- [ ] **Step 1: Create the test file**

Create `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class ReimportMeetingTranscriptHandlerTests
{
    private readonly Mock<IMeetingTranscriptRepository> _mockRepository;
    private readonly Mock<IPlaudClient> _mockPlaudClient;
    private readonly Mock<IMeetingAccessGuard> _mockAccessGuard;
    private readonly Mock<ILogger<ReimportMeetingTranscriptHandler>> _mockLogger;
    private readonly ReimportMeetingTranscriptHandler _handler;

    public ReimportMeetingTranscriptHandlerTests()
    {
        _mockRepository = new Mock<IMeetingTranscriptRepository>();
        _mockPlaudClient = new Mock<IPlaudClient>();
        _mockAccessGuard = new Mock<IMeetingAccessGuard>();
        _mockLogger = new Mock<ILogger<ReimportMeetingTranscriptHandler>>();

        _mockAccessGuard.Setup(g => g.CanAccess(It.IsAny<MeetingTranscript>())).Returns(true);

        _handler = new ReimportMeetingTranscriptHandler(
            _mockRepository.Object,
            _mockPlaudClient.Object,
            _mockAccessGuard.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Handle_WhenMeetingNotFound_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MeetingTranscript?)null);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _mockPlaudClient.Verify(c => c.GetFileDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRecordingNotGenerated_ReturnsBusinessRuleViolation()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_raw",
            Subject = "Old Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>
            {
                new() { Id = Guid.NewGuid(), MeetingTranscriptId = id, Title = "Existing Task" }
            }
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_raw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = false, SummaryAvailable = false, AudioAvailable = false });

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.BusinessRuleViolation);
        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGenerated_RefreshesSummaryTranscriptAndSubject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var existingTask = new ProposedTask { Id = Guid.NewGuid(), MeetingTranscriptId = id, Title = "Keep This Task" };
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_gen",
            Subject = "Old Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Status = MeetingTranscriptStatus.PendingReview,
            ReceivedAt = DateTime.UtcNow.AddDays(-1),
            Tasks = new List<ProposedTask> { existingTask }
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_gen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });

        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_gen", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript content");

        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_gen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult("New Headline", "New summary content"));

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();

        entity.RawTranscript.Should().Be("New transcript content");
        entity.Summary.Should().Be("New summary content");
        entity.Subject.Should().Be("New Headline");
        entity.Tasks.Should().HaveCount(1);
        entity.Tasks.Single().Title.Should().Be("Keep This Task");

        _mockRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenHeadlineIsEmpty_PreservesExistingSubject()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript
        {
            Id = id,
            PlaudRecordingId = "rec_nohdr",
            Subject = "Original Subject",
            Summary = "Old summary",
            RawTranscript = "Old transcript",
            Tasks = new List<ProposedTask>()
        };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockPlaudClient
            .Setup(c => c.GetFileDetailAsync("rec_nohdr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });

        _mockPlaudClient
            .Setup(c => c.GetTranscriptAsync("rec_nohdr", It.IsAny<CancellationToken>()))
            .ReturnsAsync("New transcript");

        _mockPlaudClient
            .Setup(c => c.GetSummaryAsync("rec_nohdr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult(string.Empty, "New summary"));

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        entity.Subject.Should().Be("Original Subject");
    }

    [Fact]
    public async Task Handle_WhenAccessDenied_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new MeetingTranscript { Id = id, PlaudRecordingId = "rec_priv", Tasks = new List<ProposedTask>() };

        _mockRepository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockAccessGuard
            .Setup(g => g.CanAccess(entity))
            .Returns(false);

        // Act
        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ResourceNotFound);
        _mockPlaudClient.Verify(c => c.GetFileDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run tests to confirm they FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Tests/ --filter "ReimportMeetingTranscriptHandlerTests" --no-build 2>&1 | tail -10
```

Expected: build error — `ReimportMeetingTranscriptHandler`, `ReimportMeetingTranscriptRequest` do not exist.

---

## Task 7: Implement ReimportMeetingTranscript use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs`

- [ ] **Step 1: Create the request**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;

public class ReimportMeetingTranscriptRequest : IRequest<ReimportMeetingTranscriptResponse>
{
    public Guid Id { get; set; }
}
```

- [ ] **Step 2: Create the response**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;

public class ReimportMeetingTranscriptResponse : BaseResponse
{
    public ReimportMeetingTranscriptResponse() { }
    public ReimportMeetingTranscriptResponse(ErrorCodes errorCode) : base(errorCode) { }
}
```

- [ ] **Step 3: Create the handler**

Create `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;

public sealed class ReimportMeetingTranscriptHandler
    : IRequestHandler<ReimportMeetingTranscriptRequest, ReimportMeetingTranscriptResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IPlaudClient _plaudClient;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<ReimportMeetingTranscriptHandler> _logger;

    public ReimportMeetingTranscriptHandler(
        IMeetingTranscriptRepository repository,
        IPlaudClient plaudClient,
        IMeetingAccessGuard accessGuard,
        ILogger<ReimportMeetingTranscriptHandler> logger)
    {
        _repository = repository;
        _plaudClient = plaudClient;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<ReimportMeetingTranscriptResponse> Handle(
        ReimportMeetingTranscriptRequest request,
        CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {Id} not found for reimport", request.Id);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {Id} for reimport", request.Id);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        var detail = await _plaudClient.GetFileDetailAsync(transcript.PlaudRecordingId, cancellationToken);
        if (!detail.IsGenerated)
        {
            _logger.LogInformation("Recording {RecordingId} not yet generated on Plaud, cannot reimport", transcript.PlaudRecordingId);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.BusinessRuleViolation);
        }

        var rawTranscript = await _plaudClient.GetTranscriptAsync(transcript.PlaudRecordingId, cancellationToken);
        var summaryResult = await _plaudClient.GetSummaryAsync(transcript.PlaudRecordingId, cancellationToken);

        transcript.RawTranscript = rawTranscript;
        transcript.Summary = summaryResult.MarkdownContent;
        if (!string.IsNullOrWhiteSpace(summaryResult.Headline))
            transcript.Subject = summaryResult.Headline;

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reimported recording {RecordingId} for transcript {TranscriptId}", transcript.PlaudRecordingId, transcript.Id);

        return new ReimportMeetingTranscriptResponse();
    }
}
```

- [ ] **Step 4: Build the solution**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet build Anela.Heblo.sln 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 5: Run the reimport handler tests to confirm they PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Tests/ --filter "ReimportMeetingTranscriptHandlerTests" 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 6: Run the full Anela.Heblo.Tests suite**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test backend/test/Anela.Heblo.Tests/ 2>&1 | tail -10
```

Expected: all passing.

---

## Task 8: Controller endpoint + commit backend

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`

- [ ] **Step 1: Add the reimport using directive**

In `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs`, add to the existing using block at the top:

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;
```

- [ ] **Step 2: Add the reimport endpoint**

In `MeetingTasksController.cs`, add the following method before the closing `}` of the class:

```csharp
    [HttpPost("{transcriptId:guid}/reimport")]
    public async Task<ActionResult<ReimportMeetingTranscriptResponse>> Reimport(
        Guid transcriptId,
        CancellationToken ct = default)
        => HandleResponse(await _mediator.Send(new ReimportMeetingTranscriptRequest { Id = transcriptId }, ct));
```

- [ ] **Step 3: Build to confirm no errors**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet build Anela.Heblo.sln 2>&1 | tail -10
```

Expected: `Build succeeded.`

- [ ] **Step 4: Run dotnet format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet format Anela.Heblo.sln 2>&1 | tail -5
```

- [ ] **Step 5: Commit backend reimport**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && git add \
  backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ \
  backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs \
  backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs && \
  git commit -m "feat: add ReimportMeetingTranscript use case and POST endpoint"
```

---

## Task 9: Frontend – add useReimportMeeting hook

**Files:**
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts`

- [ ] **Step 1: Add ReimportMeetingResponse type and useReimportMeeting hook**

In `frontend/src/api/hooks/useMeetingTasks.ts`, append the following at the bottom of the file (after `useExplainMeetingSummary`):

```typescript
// --- Reimport ---

export interface ReimportMeetingResponse {
  success: boolean;
  errorCode?: string;
}

export function useReimportMeeting() {
  const qc = useQueryClient();
  return useMutation<ReimportMeetingResponse, Error, string>({
    mutationFn: async (transcriptId) =>
      fetchJson<ReimportMeetingResponse>(
        `/api/meeting-tasks/${encodeURIComponent(transcriptId)}/reimport`,
        { method: "POST", headers: { Accept: "application/json" } },
      ),
    onSuccess: (_d, transcriptId) => {
      qc.invalidateQueries({ queryKey: MEETING_TASKS_KEYS.detail(transcriptId) });
    },
  });
}
```

- [ ] **Step 2: Verify frontend builds**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat/frontend && npm run build 2>&1 | tail -15
```

Expected: build completes without TypeScript errors.

---

## Task 10: Frontend – add Reimport button to MeetingTaskDetailPage

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`

- [ ] **Step 1: Add RefreshCw to imports and import useReimportMeeting**

In `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`:

Replace the lucide-react import line:
```typescript
import {
  ArrowLeft, Check, X, Plus, Send, CheckCheck, Clock, CheckCircle, CheckCircle2,
  ChevronDown, ChevronRight, AlertTriangle,
} from "lucide-react";
```

With:
```typescript
import {
  ArrowLeft, Check, X, Plus, Send, CheckCheck, Clock, CheckCircle, CheckCircle2,
  ChevronDown, ChevronRight, AlertTriangle, RefreshCw,
} from "lucide-react";
```

In the same file, add `useReimportMeeting` to the useMeetingTasks import:
```typescript
import {
  MeetingUserDto,
  ProposedTaskDto,
  ProposedTaskStatus,
  TaskFormData,
  TranscriptStatus,
  useAddProposedTask,
  useExplainMeetingSummary,
  useMeetingTaskDetail,
  useMeetingUsers,
  useReimportMeeting,
  useSubmitToTodo,
  useUpdateProposedTask,
  useUpdateProposedTaskStatus,
} from "../../../api/hooks/useMeetingTasks";
```

- [ ] **Step 2: Add reimport state and mutation inside the component**

In the `MeetingTaskDetailPage` component body, after the line `const [accessModalOpen, setAccessModalOpen] = useState(false);`, add:

```typescript
  const reimport = useReimportMeeting();
  const [reimportError, setReimportError] = useState<string | null>(null);
```

- [ ] **Step 3: Add handleReimport handler**

After the `handleCloseExplain` function, add:

```typescript
  const handleReimport = async () => {
    setReimportError(null);
    try {
      await reimport.mutateAsync(id);
    } catch {
      setReimportError("Reimport se nezdařil. Nahrávka pravděpodobně ještě není zpracována na straně Plaud.");
    }
  };
```

- [ ] **Step 4: Add Reimport button to the header actions**

In the header section, inside the `<div className="flex items-center gap-2 shrink-0">` that already contains the status badge and "Spravovat přístup" button, add the Reimport button **before** the `{isMeetingManager && (…)}` block:

```tsx
          <button
            type="button"
            onClick={handleReimport}
            disabled={reimport.isPending}
            className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <RefreshCw className={`w-4 h-4 mr-1 ${reimport.isPending ? 'animate-spin' : ''}`} />
            Reimport
          </button>
```

- [ ] **Step 5: Add reimport error display**

After the header `<div className="px-4 sm:px-6 lg:px-8 flex items-start justify-between gap-4">` block and before the summary section, add:

```tsx
      {reimportError && (
        <div className="px-4 sm:px-6 lg:px-8 mt-2">
          <p className="text-sm text-red-600">{reimportError}</p>
        </div>
      )}
```

- [ ] **Step 6: Build and lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat/frontend && npm run build 2>&1 | tail -15 && npm run lint 2>&1 | tail -15
```

Expected: both complete without errors.

- [ ] **Step 7: Commit frontend changes**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && git add \
  frontend/src/api/hooks/useMeetingTasks.ts \
  frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx && \
  git commit -m "feat: add Reimport button on meeting detail page"
```

---

## Final Verification

- [ ] **Full backend build + format**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet build Anela.Heblo.sln && dotnet format Anela.Heblo.sln 2>&1 | tail -10
```

- [ ] **All backend tests pass**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat && dotnet test Anela.Heblo.sln 2>&1 | tail -15
```

Expected: all test projects pass, zero failures.

- [ ] **Frontend build + lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/surat/frontend && npm run build && npm run lint 2>&1 | tail -10
```

Expected: clean.
