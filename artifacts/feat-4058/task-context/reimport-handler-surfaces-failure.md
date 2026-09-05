### task: reimport-handler-surfaces-failure

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs`

This task depends on `extractor-retry-and-recovery`.

- [ ] **Step 1: Write the failing test**

Add to `ReimportMeetingTranscriptHandlerTests.cs` (copy the existing happy-path test's arrange block for repository/access-guard/plaud-client setup verbatim, changing only the extractor mock and assertions):

```csharp
    [Fact]
    public async Task Handle_WhenExtractionFailsAfterRetries_ReturnsExceptionErrorAndDoesNotReplaceTasks()
    {
        // Arrange (mirror the existing happy-path test's setup for transcript/access/plaud client)
        _mockExtractor
            .Setup(x => x.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingTaskExtractionFailedException("boom", 3, "not-json"));

        var response = await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = transcript.Id }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Exception);
        _mockRepository.Verify(
            x => x.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<List<ProposedTask>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

(Use the exact local variable name and setup the existing happy-path test in this file uses for the transcript fixture and mocks — do not re-declare fields that already exist in the test class's constructor.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests"`
Expected: FAIL — exception currently propagates uncaught.

- [ ] **Step 3: Write minimal implementation**

In `ReimportMeetingTranscriptHandler.cs`, replace:

```csharp
        var extraction = await _extractor.ExtractAsync(summaryResult.MarkdownContent, rawTranscript, cancellationToken);
        transcript.Participants = extraction.Participants;
```

with:

```csharp
        MeetingExtractionResult extraction;
        try
        {
            extraction = await _extractor.ExtractAsync(summaryResult.MarkdownContent, rawTranscript, cancellationToken);
        }
        catch (MeetingTaskExtractionFailedException ex)
        {
            _logger.LogError(ex,
                "Meeting task extraction failed for transcript {TranscriptId} after {AttemptCount} attempts",
                transcript.Id, ex.AttemptCount);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.Exception);
        }

        transcript.Participants = extraction.Participants;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests"`
Expected: PASS (all tests, old and new)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs
git commit -m "fix(meeting-tasks): surface extraction failure on transcript reimport instead of silently clearing tasks"
```
