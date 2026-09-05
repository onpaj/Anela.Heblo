### task: ingest-handler-surfaces-failure

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`

This task depends on `extractor-retry-and-recovery` (the handler now must handle `MeetingTaskExtractionFailedException`).

- [ ] **Step 1: Write the failing test**

Add to `IngestPlaudRecordingHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_WhenExtractionFailsAfterRetries_ReturnsFailureWithoutPersistingTranscript()
    {
        _mockPlaudClient
            .Setup(x => x.GetFileDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudFileDetail { IsGenerated = true });
        _mockPlaudClient
            .Setup(x => x.GetTranscriptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("transcript text");
        _mockPlaudClient
            .Setup(x => x.GetSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaudSummaryResult { MarkdownContent = "summary", Headline = "Headline" });
        _mockRepository
            .Setup(x => x.ExistsByPlaudIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository
            .Setup(x => x.IsPlaudRecordingDeletedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockExtractor
            .Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingTaskExtractionFailedException("boom", 3, "not-json"));

        var request = new IngestPlaudRecordingRequest
        {
            PlaudRecordingId = "rec_fail",
            Name = "Failing Meeting",
            PlaudCreatedAt = DateTime.UtcNow
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Skipped.Should().BeFalse();
        _mockRepository.Verify(
            x => x.AddAsync(It.IsAny<MeetingTranscript>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

(Adjust the exact mock setup for `PlaudFileDetail`/`PlaudSummaryResult` construction to match whatever shape the existing "happy path" test in this file already uses — copy its arrange block for `GetFileDetailAsync`/`GetTranscriptAsync`/`GetSummaryAsync`/`ExistsByPlaudIdAsync`/`IsPlaudRecordingDeletedAsync` verbatim rather than retyping it, since this test only needs to differ in the extractor's setup and the assertions.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests"`
Expected: FAIL — the exception currently propagates uncaught out of `Handle`, so the test either fails on an unhandled exception or `response.Success` assertion never runs.

- [ ] **Step 3: Write minimal implementation**

In `IngestPlaudRecordingHandler.cs`, replace:

```csharp
        // Extract tasks and participants using the meeting task extractor
        var extraction = await _extractor.ExtractAsync(summaryResult.MarkdownContent, transcript, cancellationToken);
```

with:

```csharp
        // Extract tasks and participants using the meeting task extractor
        MeetingExtractionResult extraction;
        try
        {
            extraction = await _extractor.ExtractAsync(summaryResult.MarkdownContent, transcript, cancellationToken);
        }
        catch (MeetingTaskExtractionFailedException ex)
        {
            _logger.LogError(ex,
                "Meeting task extraction failed for recording {RecordingId} after {AttemptCount} attempts",
                request.PlaudRecordingId, ex.AttemptCount);
            return new IngestPlaudRecordingResponse { Success = false };
        }
```

In `PlaudPollingJob.cs`, update the per-recording result handling so a caught failure is not miscounted as "ingested". Replace:

```csharp
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
```

with:

```csharp
                var response = await _mediator.Send(request, cancellationToken);

                if (response.Skipped)
                {
                    if (response.NotGenerated)
                        notGenerated++;
                    else
                        skipped++;
                }
                else if (!response.Success)
                {
                    failed++;
                }
                else
                {
                    ingested++;
                }
```

and add `int failed = 0;` alongside the existing `int ingested = 0; int skipped = 0; int notGenerated = 0;` declarations, and include it in the final summary log:

```csharp
        _logger.LogInformation(
            "{JobName} complete. {Ingested} new recordings ingested, {Skipped} already known, {NotGenerated} not yet generated, {Failed} failed extraction",
            Metadata.JobName, ingested, skipped, notGenerated, failed);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests"`
Expected: PASS (all tests, old and new)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs
git commit -m "fix(meeting-tasks): surface extraction failure as a failed ingest instead of a false success"
```
