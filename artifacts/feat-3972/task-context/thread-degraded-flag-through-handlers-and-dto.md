### task: thread-degraded-flag-through-handlers-and-dto

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs` (line 82, and its `Verify` predicate in the test at lines 97-102)
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs` (line 92)
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs` (after line 19)
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs` (after line 71)
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs` (after line 69)
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs`

Reference files read to produce this task (do not modify):
- `IngestPlaudRecordingHandler.cs` — confirmed the entity object initializer (lines 72-97) sets
  `Participants = extraction.Participants,` at line 82, inside the same `new MeetingTranscript {
  ... }` block that also builds `Tasks`.
- `ReimportMeetingTranscriptHandler.cs` — confirmed `transcript.Participants =
  extraction.Participants;` at line 92, immediately after `var extraction = await
  _extractor.ExtractAsync(...)` at line 91, and before `var newTasks = extraction.Tasks...` at
  line 93.
- `MeetingTranscriptDto.cs` — confirmed this is already a plain `class` (not a record) with
  `public List<string> Participants { get; set; } = new();` as its line 19 (last property before
  `AccessLevel`) — the DTOs-must-be-classes rule is already satisfied; this task only adds one
  more `bool` property in the same style.
- `GetTranscriptDetailHandler.cs` — confirmed the `dto` object initializer's
  `Participants = transcript.Participants,` at line 71, immediately before `AccessLevel =
  transcript.AccessLevel.ToString(),` at line 72.
- `GetTranscriptListHandler.cs` — confirmed the `dtos` `Select(...)` projection does **not**
  currently map `Participants` at all (list rows don't need it); `AccessLevel = t.AccessLevel.ToString(),`
  is at line 69, immediately before `Tasks = new()` at line 70.
- `IngestPlaudRecordingHandlerTests.cs` — confirmed
  `Handle_WithNewRecording_CreatesTranscriptAndTasksInPendingReviewState` (lines 36-109) captures
  the saved entity via `_mockRepository.Verify(r => r.AddAsync(It.Is<MeetingTranscript>(t => ...`
  (lines 95-104), which is where a `TasksExtractionDegraded` assertion can be added inline.
- `ReimportMeetingTranscriptHandlerTests.cs` — confirmed the constructor already wires
  `_mockPlaudClient.Setup(c => c.ListRecentAsync(...)).ReturnsAsync(new
  List<PlaudRecordingSummary>());` (lines 33-35) as a default, so new tests using a
  `PlaudRecordingId` not otherwise stubbed for `ListRecentAsync` still succeed via that default.
- `GetTranscriptDetailHandlerTests.cs` / `GetTranscriptListHandlerTests.cs` — confirmed both use
  `NullLogger<T>.Instance` (no logger mocking needed) and a simple `MeetingTranscript` entity /
  repository-mock-return pattern for new one-off mapping assertions.

Steps:

- [ ] **Step 1: Write a failing test asserting `IngestPlaudRecordingHandler` sets the flag from a degraded extraction.**
  Add to `IngestPlaudRecordingHandlerTests.cs`:
  ```csharp
  [Fact]
  public async Task Handle_WhenExtractionDegraded_SetsTasksExtractionDegradedOnEntity()
  {
      // Arrange
      var recordingId = "rec_degraded";
      var request = new IngestPlaudRecordingRequest
      {
          PlaudRecordingId = recordingId,
          Name = "Degraded Meeting",
          PlaudCreatedAt = DateTime.UtcNow
      };

      _mockRepository
          .Setup(r => r.ExistsByPlaudIdAsync(recordingId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);
      _mockPlaudClient
          .Setup(c => c.GetFileDetailAsync(recordingId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
      _mockPlaudClient
          .Setup(c => c.GetTranscriptAsync(recordingId, It.IsAny<CancellationToken>()))
          .ReturnsAsync("transcript text");
      _mockPlaudClient
          .Setup(c => c.GetSummaryAsync(recordingId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PlaudSummaryResult("Headline", "summary text"));
      _mockExtractor
          .Setup(e => e.ExtractAsync("summary text", "transcript text", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MeetingExtractionResult(new List<ExtractedTask>(), new List<string>(), Degraded: true));

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
      saved!.TasksExtractionDegraded.Should().BeTrue();
  }
  ```
  Also add `&& !t.TasksExtractionDegraded` to the existing `Verify` predicate in
  `Handle_WithNewRecording_CreatesTranscriptAndTasksInPendingReviewState` (line 102), so the
  non-degraded happy path is covered too:
  ```csharp
                  t.Tasks.All(task => task.MeetingTranscriptId == t.Id && task.Status == ProposedTaskStatus.Pending && !task.IsManuallyAdded) &&
                  !t.TasksExtractionDegraded),
  ```

- [ ] **Step 2: Run the new test and confirm it fails (RED — the entity initializer doesn't set the field yet).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests"
  ```

- [ ] **Step 3: Implement — set the flag in `IngestPlaudRecordingHandler`.**
  Edit the entity initializer (after line 82, `Participants = extraction.Participants,`):
  ```csharp
              Participants = extraction.Participants,
              TasksExtractionDegraded = extraction.Degraded,
  ```

- [ ] **Step 4: Write failing tests asserting `ReimportMeetingTranscriptHandler` overwrites (not
  OR-merges) the flag in both directions.**
  Add to `ReimportMeetingTranscriptHandlerTests.cs`:
  ```csharp
  [Fact]
  public async Task Handle_WhenExtractionDegraded_SetsTasksExtractionDegradedOnTranscript()
  {
      // Arrange
      var id = Guid.NewGuid();
      var entity = new MeetingTranscript
      {
          Id = id,
          PlaudRecordingId = "rec_degrade_set",
          Subject = "Subject",
          Summary = "Old summary",
          RawTranscript = "Old transcript",
          TasksExtractionDegraded = false,
          Tasks = new List<ProposedTask>()
      };

      _mockRepository
          .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
          .ReturnsAsync(entity);
      _mockPlaudClient
          .Setup(c => c.GetFileDetailAsync("rec_degrade_set", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
      _mockPlaudClient
          .Setup(c => c.GetTranscriptAsync("rec_degrade_set", It.IsAny<CancellationToken>()))
          .ReturnsAsync("Transcript");
      _mockPlaudClient
          .Setup(c => c.GetSummaryAsync("rec_degrade_set", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PlaudSummaryResult("Headline", "Summary"));
      _mockExtractor
          .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MeetingExtractionResult(new List<ExtractedTask>(), new List<string>(), Degraded: true));
      _mockRepository
          .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      _mockRepository
          .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);

      // Act
      await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

      // Assert
      entity.TasksExtractionDegraded.Should().BeTrue();
  }

  [Fact]
  public async Task Handle_WhenReimportSucceedsCleanly_ClearsPreviouslySetDegradedFlag()
  {
      // Arrange
      var id = Guid.NewGuid();
      var entity = new MeetingTranscript
      {
          Id = id,
          PlaudRecordingId = "rec_degrade_clear",
          Subject = "Subject",
          Summary = "Old summary",
          RawTranscript = "Old transcript",
          TasksExtractionDegraded = true,
          Tasks = new List<ProposedTask>()
      };

      _mockRepository
          .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
          .ReturnsAsync(entity);
      _mockPlaudClient
          .Setup(c => c.GetFileDetailAsync("rec_degrade_clear", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PlaudFileDetail { TranscriptAvailable = true, SummaryAvailable = true, AudioAvailable = true });
      _mockPlaudClient
          .Setup(c => c.GetTranscriptAsync("rec_degrade_clear", It.IsAny<CancellationToken>()))
          .ReturnsAsync("Transcript");
      _mockPlaudClient
          .Setup(c => c.GetSummaryAsync("rec_degrade_clear", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new PlaudSummaryResult("Headline", "Summary"));
      _mockExtractor
          .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(new MeetingExtractionResult(new List<ExtractedTask>(), new List<string>(), Degraded: false));
      _mockRepository
          .Setup(r => r.ReplacePendingTasksAsync(It.IsAny<MeetingTranscript>(), It.IsAny<IReadOnlyList<ProposedTask>>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      _mockRepository
          .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);

      // Act
      await _handler.Handle(new ReimportMeetingTranscriptRequest { Id = id }, CancellationToken.None);

      // Assert
      entity.TasksExtractionDegraded.Should().BeFalse();
  }
  ```

- [ ] **Step 5: Run the new tests and confirm they fail (RED).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReimportMeetingTranscriptHandlerTests"
  ```

- [ ] **Step 6: Implement — overwrite the flag in `ReimportMeetingTranscriptHandler`.**
  Edit after line 92 (`transcript.Participants = extraction.Participants;`):
  ```csharp
          transcript.Participants = extraction.Participants;
          transcript.TasksExtractionDegraded = extraction.Degraded;
  ```
  This is an unconditional overwrite (never OR-merged with the prior value), per Decision 3 in the
  architecture review — matching the existing pattern where `Participants` and the pending-task
  set are already unconditionally replaced on every reimport.

- [ ] **Step 7: Add the field to the DTO.**
  Edit `MeetingTranscriptDto.cs` — insert after line 19 (`public List<string> Participants { get; set; } = new();`):
  ```csharp
      public bool TasksExtractionDegraded { get; set; }
  ```

- [ ] **Step 8: Write failing tests asserting the detail and list handlers map the new field.**
  Add to `GetTranscriptDetailHandlerTests.cs`:
  ```csharp
  [Fact]
  public async Task Handle_MapsTasksExtractionDegraded()
  {
      // Arrange
      var transcriptId = Guid.NewGuid();
      var transcript = new MeetingTranscript
      {
          Id = transcriptId,
          PlaudRecordingId = "rec_1",
          PlaudCreatedAt = DateTime.UtcNow,
          Subject = "Subject",
          Summary = "Summary",
          RawTranscript = "",
          Status = MeetingTranscriptStatus.PendingReview,
          ReceivedAt = DateTime.UtcNow,
          TasksExtractionDegraded = true,
          Tasks = new List<ProposedTask>()
      };
      _repositoryMock
          .Setup(r => r.GetByIdAsync(transcriptId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(transcript);

      // Act
      var result = await _handler.Handle(
          new GetTranscriptDetailRequest { Id = transcriptId }, CancellationToken.None);

      // Assert
      result.Transcript!.TasksExtractionDegraded.Should().BeTrue();
  }
  ```
  Add to `GetTranscriptListHandlerTests.cs`:
  ```csharp
  [Fact]
  public async Task Handle_MapsTasksExtractionDegraded()
  {
      // Arrange
      var transcriptId = Guid.NewGuid();
      var transcript = new MeetingTranscript
      {
          Id = transcriptId,
          PlaudRecordingId = "rec-002",
          PlaudCreatedAt = DateTime.UtcNow,
          Subject = "Degraded Meeting",
          Summary = "Summary",
          RawTranscript = "",
          Status = MeetingTranscriptStatus.PendingReview,
          ReceivedAt = DateTime.UtcNow,
          TasksExtractionDegraded = true,
          Tasks = new List<ProposedTask>()
      };

      _repositoryMock
          .Setup(r => r.GetListAsync(
              It.IsAny<MeetingTranscriptStatus?>(),
              It.IsAny<string?>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<string?>(),
              It.IsAny<int>(),
              It.IsAny<int>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync((new List<MeetingTranscript> { transcript }, 1));

      var request = new GetTranscriptListRequest { PageNumber = 1, PageSize = 20 };

      // Act
      var result = await _handler.Handle(request, CancellationToken.None);

      // Assert
      result.Items[0].TasksExtractionDegraded.Should().BeTrue();
  }
  ```

- [ ] **Step 9: Run the new tests and confirm they fail (RED — the handlers don't map the field yet).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GetTranscriptDetailHandlerTests|FullyQualifiedName~GetTranscriptListHandlerTests"
  ```

- [ ] **Step 10: Implement — map the field in both read handlers.**
  Edit `GetTranscriptDetailHandler.cs` — insert after line 71 (`Participants = transcript.Participants,`):
  ```csharp
              Participants = transcript.Participants,
              TasksExtractionDegraded = transcript.TasksExtractionDegraded,
  ```
  Edit `GetTranscriptListHandler.cs` — insert after line 69 (`AccessLevel = t.AccessLevel.ToString(),`):
  ```csharp
              AccessLevel = t.AccessLevel.ToString(),
              TasksExtractionDegraded = t.TasksExtractionDegraded,
  ```

- [ ] **Step 11: Run all four affected test files and confirm everything passes (GREEN).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~IngestPlaudRecordingHandlerTests|FullyQualifiedName~ReimportMeetingTranscriptHandlerTests|FullyQualifiedName~GetTranscriptDetailHandlerTests|FullyQualifiedName~GetTranscriptListHandlerTests"
  ```

- [ ] **Step 12: Build and format.**
  ```bash
  dotnet build
  dotnet format
  ```

- [ ] **Step 13: Commit.**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs \
          backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs \
          backend/src/Anela.Heblo.Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs \
          backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/GetTranscriptDetailHandler.cs \
          backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/GetTranscriptList/GetTranscriptListHandler.cs \
          backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs \
          backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ReimportMeetingTranscriptHandlerTests.cs \
          backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptDetailHandlerTests.cs \
          backend/test/Anela.Heblo.Tests/Features/MeetingTasks/GetTranscriptListHandlerTests.cs
  git commit -m "Thread TasksExtractionDegraded through ingest/reimport handlers and read DTOs"
  ```

---
