# Plaud Reimport: Refresh Title from Recording Name — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Reimport so it refreshes the meeting title from the current Plaud recording name, and align ingest to prefer the human name over the auto-generated summary headline.

**Architecture:** `ReimportMeetingTranscriptHandler` gains a `ListRecentAsync` lookup after fetching transcript+summary; subject priority becomes recording name > headline > keep existing. `IngestPlaudRecordingHandler` flips its existing priority from headline-first to name-first. No new types, no new files.

**Tech Stack:** .NET 8, C#, MediatR, xUnit, FluentAssertions, Moq.

---

## File Map

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs` | Add recording-name lookup via `ListRecentAsync`; apply name > headline > keep priority |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs` | Flip subject priority to name-first |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs` | Add default `ListRecentAsync` mock to constructor; add 4 new tests |
| `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs` | Add 1 new test: recording name wins over headline |

**Reused without change:** `IPlaudClient.ListRecentAsync`, `PlaudRecordingSummary.Name`, `MeetingTranscript.PlaudCreatedAt`.

---

## Task 1: Add Failing Tests — Reimport Handler

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs`

- [ ] **Step 1: Add a default `ListRecentAsync` mock to the constructor**

In the constructor (after the existing `_mockAccessGuard` setup at line 26), add:

```csharp
_mockPlaudClient
    .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new List<PlaudRecordingSummary>());
```

This keeps the existing tests passing once the handler starts calling `ListRecentAsync` (empty list → no name found → falls through to headline/preserve logic they already assert).

- [ ] **Step 2: Add test — recording name present beats headline**

Append this `[Fact]` to the class:

```csharp
[Fact]
public async Task Handle_WhenRecordingNamePresent_UsesRecordingNameAsSubject()
{
    // Arrange
    var id = Guid.NewGuid();
    var entity = new MeetingTranscript
    {
        Id = id,
        PlaudRecordingId = "rec_named",
        PlaudCreatedAt = DateTime.UtcNow.AddDays(-2),
        Subject = "Old Subject",
        Summary = "Old summary",
        RawTranscript = "Old transcript",
        Tasks = new List<ProposedTask>()
    };

    _mockRepository
        .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entity);
    _mockPlaudClient
        .Setup(c => c.GetFileDetailAsync("rec_named", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
    _mockPlaudClient
        .Setup(c => c.GetTranscriptAsync("rec_named", It.IsAny<CancellationToken>()))
        .ReturnsAsync("New transcript");
    _mockPlaudClient
        .Setup(c => c.GetSummaryAsync("rec_named", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudSummaryResult("Auto Headline", "New summary"));
    _mockPlaudClient
        .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PlaudRecordingSummary>
        {
            new() { Id = "rec_named", Name = "Týmová porada: letní plány", CreatedAt = entity.PlaudCreatedAt }
        });
    _mockRepository
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    entity.Subject.Should().Be("Týmová porada: letní plány");
}
```

- [ ] **Step 3: Add test — empty recording name falls through to headline**

```csharp
[Fact]
public async Task Handle_WhenRecordingNameEmpty_UsesHeadlineAsSubject()
{
    // Arrange
    var id = Guid.NewGuid();
    var entity = new MeetingTranscript
    {
        Id = id,
        PlaudRecordingId = "rec_noname",
        PlaudCreatedAt = DateTime.UtcNow.AddDays(-1),
        Subject = "Old Subject",
        Summary = "Old summary",
        RawTranscript = "Old transcript",
        Tasks = new List<ProposedTask>()
    };

    _mockRepository
        .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entity);
    _mockPlaudClient
        .Setup(c => c.GetFileDetailAsync("rec_noname", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
    _mockPlaudClient
        .Setup(c => c.GetTranscriptAsync("rec_noname", It.IsAny<CancellationToken>()))
        .ReturnsAsync("New transcript");
    _mockPlaudClient
        .Setup(c => c.GetSummaryAsync("rec_noname", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudSummaryResult("Auto Headline", "New summary"));
    _mockPlaudClient
        .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<PlaudRecordingSummary>
        {
            new() { Id = "rec_noname", Name = string.Empty, CreatedAt = entity.PlaudCreatedAt }
        });
    _mockRepository
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

    // Assert
    entity.Subject.Should().Be("Auto Headline");
}
```

- [ ] **Step 4: Add test — `ListRecentAsync` throws; Reimport still succeeds and uses headline**

```csharp
[Fact]
public async Task Handle_WhenListRecentThrows_StillSucceedsAndFallsBackToHeadline()
{
    // Arrange
    var id = Guid.NewGuid();
    var entity = new MeetingTranscript
    {
        Id = id,
        PlaudRecordingId = "rec_apierr",
        PlaudCreatedAt = DateTime.UtcNow.AddDays(-1),
        Subject = "Old Subject",
        Summary = "Old summary",
        RawTranscript = "Old transcript",
        Tasks = new List<ProposedTask>()
    };

    _mockRepository
        .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(entity);
    _mockPlaudClient
        .Setup(c => c.GetFileDetailAsync("rec_apierr", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
    _mockPlaudClient
        .Setup(c => c.GetTranscriptAsync("rec_apierr", It.IsAny<CancellationToken>()))
        .ReturnsAsync("New transcript");
    _mockPlaudClient
        .Setup(c => c.GetSummaryAsync("rec_apierr", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudSummaryResult("Fallback Headline", "New summary"));
    _mockPlaudClient
        .Setup(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("Plaud API unavailable"));
    _mockRepository
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    entity.RawTranscript.Should().Be("New transcript");
    entity.Summary.Should().Be("New summary");
    entity.Subject.Should().Be("Fallback Headline");
}
```

- [ ] **Step 5: Add `using System.Net.Http;` to the test file imports if not present**

Check the top of the file. If `System.Net.Http` is not already imported, add it so `HttpRequestException` resolves.

- [ ] **Step 6: Run only the Reimport tests to confirm they fail for the right reason**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/copenhagen
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests" \
  --no-build 2>&1 | tail -40
```

Expected: 3 new tests FAIL with `NullReferenceException` or mock invocation failure on `ListRecentAsync` (handler doesn't call it yet). Existing 5 tests may also fail for the same reason. That is correct — the implementation hasn't changed yet.

- [ ] **Step 7: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs
git commit -m "test(meeting-tasks): add failing tests for recording-name title priority in reimport"
```

---

## Task 2: Implement — Reimport Handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs`

- [ ] **Step 1: Replace lines 53-59 with the new recording-name lookup and priority logic**

Current code (lines 53-59):
```csharp
        var rawTranscript = await _plaudClient.GetTranscriptAsync(transcript.PlaudRecordingId, cancellationToken);
        var summaryResult = await _plaudClient.GetSummaryAsync(transcript.PlaudRecordingId, cancellationToken);

        transcript.RawTranscript = rawTranscript;
        transcript.Summary = summaryResult.MarkdownContent;
        if (!string.IsNullOrWhiteSpace(summaryResult.Headline))
            transcript.Subject = summaryResult.Headline;
```

Replace with:
```csharp
        var rawTranscript = await _plaudClient.GetTranscriptAsync(transcript.PlaudRecordingId, cancellationToken);
        var summaryResult = await _plaudClient.GetSummaryAsync(transcript.PlaudRecordingId, cancellationToken);

        string? recordingName = null;
        try
        {
            var days = Math.Clamp(
                (int)(DateTime.UtcNow.Date - transcript.PlaudCreatedAt.Date).TotalDays + 1,
                1, 365);
            var recent = await _plaudClient.ListRecentAsync(days, cancellationToken);
            recordingName = recent.FirstOrDefault(r => r.Id == transcript.PlaudRecordingId)?.Name;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch recording name for {RecordingId}, falling back to headline",
                transcript.PlaudRecordingId);
        }

        transcript.RawTranscript = rawTranscript;
        transcript.Summary = summaryResult.MarkdownContent;

        if (!string.IsNullOrWhiteSpace(recordingName))
            transcript.Subject = recordingName;
        else if (!string.IsNullOrWhiteSpace(summaryResult.Headline))
            transcript.Subject = summaryResult.Headline;
        // else: leave transcript.Subject unchanged
```

- [ ] **Step 2: Build to confirm no compile errors**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/copenhagen
dotnet build backend/src/Anela.Heblo.Application/ 2>&1 | tail -20
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run all Reimport tests — all should now pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests" \
  --no-build 2>&1 | tail -20
```

Expected: all 9 tests (5 existing + 4 new) pass.

- [ ] **Step 4: Run formatter**

```bash
dotnet format backend/src/Anela.Heblo.Application/ 2>&1
```

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs
git commit -m "feat(meeting-tasks): refresh title from Plaud recording name on reimport"
```

---

## Task 3: Add Failing Test — Ingest Handler Priority

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`

- [ ] **Step 1: Append the new test**

```csharp
[Fact]
public async Task Handle_WhenRecordingNamePresent_RecordingNameWinsOverHeadline()
{
    // Arrange
    var request = new IngestPlaudRecordingRequest
    {
        PlaudRecordingId = "rec_named",
        Name = "Týmová porada: Z-boxy a dopravci",
        PlaudCreatedAt = DateTime.UtcNow
    };

    _mockRepository
        .Setup(r => r.ExistsByPlaudIdAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);
    _mockPlaudClient
        .Setup(c => c.GetFileDetailAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
    _mockPlaudClient
        .Setup(c => c.GetTranscriptAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
        .ReturnsAsync("transcript");
    _mockPlaudClient
        .Setup(c => c.GetSummaryAsync(request.PlaudRecordingId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlaudSummaryResult("2026-05-18 10:25:12", "summary"));
    _mockExtractor
        .Setup(e => e.ExtractAsync("summary", "transcript", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<ExtractedTask>());

    MeetingTranscript? saved = null;
    _mockRepository
        .Setup(r => r.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()))
        .Callback<MeetingTranscript, CancellationToken>((t, _) => saved = t);
    _mockRepository
        .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    // Act
    await _handler.Handle(request, CancellationToken.None);

    // Assert
    saved.Should().NotBeNull();
    saved!.Subject.Should().Be("Týmová porada: Z-boxy a dopravci");
}
```

- [ ] **Step 2: Run only Ingest tests to confirm the new test fails**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/copenhagen
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests" \
  --no-build 2>&1 | tail -20
```

Expected: `Handle_WhenRecordingNamePresent_RecordingNameWinsOverHeadline` FAILS — saved subject is `"2026-05-18 10:25:12"` (headline wins under current code). All other 5 ingest tests pass.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs
git commit -m "test(meeting-tasks): add failing test for recording-name priority in ingest"
```

---

## Task 4: Implement — Ingest Handler Priority Flip

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`

- [ ] **Step 1: Flip the subject priority on lines 57-60**

Current code (lines 57-60):
```csharp
        // Prefer the summary headline as subject; fall back to the recording name from the listing
        var subject = !string.IsNullOrWhiteSpace(summaryResult.Headline)
            ? summaryResult.Headline
            : request.Name;
```

Replace with:
```csharp
        // Prefer the human-set recording name; fall back to the summary headline
        var subject = !string.IsNullOrWhiteSpace(request.Name)
            ? request.Name
            : summaryResult.Headline;
```

- [ ] **Step 2: Build**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/copenhagen
dotnet build backend/src/Anela.Heblo.Application/ 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run all Ingest tests — all 6 should pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests" \
  --no-build 2>&1 | tail -20
```

Expected: all 6 tests pass.

Note: `Handle_WithNewRecording_CreatesTranscriptAndTasksInPendingReviewState` uses `Name = "Test Meeting"` and headline `"Test Headline"` but does not assert on `Subject`, so the priority flip does not break it.

- [ ] **Step 4: Run the full MeetingTasks test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ \
  --filter "FullyQualifiedName~MeetingTasks" \
  --no-build 2>&1 | tail -20
```

Expected: all tests pass.

- [ ] **Step 5: Run dotnet format**

```bash
dotnet format backend/src/Anela.Heblo.Application/ 2>&1
```

- [ ] **Step 6: Final build of the whole backend**

```bash
dotnet build backend/ 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs
git commit -m "feat(meeting-tasks): prefer recording name over headline as meeting subject on ingest"
```

---

## Verification Checklist

- [ ] `dotnet build` — 0 errors
- [ ] `dotnet format` — no changes left
- [ ] All 15 MeetingTasks tests pass (9 Reimport + 6 Ingest)
- [ ] Manual on staging: open meeting `6b08004cf7e0833fe4f5647f7b02e054`, click **Reimport**, title changes from `"2026-05-18 10:25:12"` to `"05-18 Týmová porada: ..."`. Detail page re-renders (handled by `useReimportMeeting` query invalidation already in place).
