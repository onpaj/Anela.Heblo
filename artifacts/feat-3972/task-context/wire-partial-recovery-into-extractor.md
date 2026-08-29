### task: wire-partial-recovery-into-extractor

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs` (lines 76-80, as left by the first task)
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs`

Reference files read to produce this task (do not modify):
- `ClaudeMeetingTaskExtractor.cs` — confirmed `NormalizeParticipants(List<string>?
  participants)` (lines 89-100) is a `private static` method on this class already applied to the
  happy-path participants list; reusing it after salvage keeps trim/dedupe behavior identical to
  the happy path with no duplicated logic.
- `PartialExtractionParser.cs` (from the previous task) — confirmed `TrySalvage(string text,
  ILogger logger)` returns `(List<ExtractedTask> Tasks, List<string> Participants, bool
  LocatedAnyArray)`, and that its `Participants` are the raw deserialized strings (untrimmed,
  undeduplicated) — normalization is left to the caller.

Steps:

- [ ] **Step 1: Write a failing extractor-level test for FR-2(a) — partial salvage.**
  Add to `ClaudeMeetingTaskExtractorTests.cs`:
  ```csharp
  private const string ThreeTasksOneWithInvalidEscape = """
      {"participants":["Alice","Bob"],"tasks":[
        {"title":"Good Task","description":"fine","assignee":"Alice","assigneeEmail":null,"dueDate":null},
        {"title":"Bad\-Task","description":"broken escape","assignee":"Bob","assigneeEmail":null,"dueDate":null},
        {"title":"Another Good","description":"ok","assignee":"Alice","assigneeEmail":null,"dueDate":null}
      ]}
      """;

  [Fact]
  public async Task ExtractAsync_WhenOneTaskHasInvalidEscape_SalvagesOthersAndFlagsDegraded()
  {
      SetupResponse(ThreeTasksOneWithInvalidEscape);

      var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

      result.Tasks.Should().HaveCount(2);
      result.Tasks[0].Title.Should().Be("Good Task");
      result.Tasks[1].Title.Should().Be("Another Good");
      result.Participants.Should().Equal("Alice", "Bob");
      result.Degraded.Should().BeTrue();
  }
  ```

- [ ] **Step 2: Run the test and confirm it fails (RED — current catch block still returns empty lists).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests.ExtractAsync_WhenOneTaskHasInvalidEscape_SalvagesOthersAndFlagsDegraded"
  ```

- [ ] **Step 3: Implement — call `PartialExtractionParser.TrySalvage` from the catch block.**
  Edit the catch block in `ClaudeMeetingTaskExtractor.cs` from:
  ```csharp
  catch (JsonException ex)
  {
      _logger.LogError(ex, "Meeting task extraction returned malformed JSON — transcript will be imported without tasks. Raw response: {RawResponse}", text);
      return new MeetingExtractionResult([], [], Degraded: true);
  }
  ```
  to:
  ```csharp
  catch (JsonException ex)
  {
      _logger.LogError(ex, "Meeting task extraction returned malformed JSON — transcript will be imported without tasks. Raw response: {RawResponse}", text);
      var (salvagedTasks, salvagedParticipants, _) = PartialExtractionParser.TrySalvage(text, _logger);
      return new MeetingExtractionResult(salvagedTasks, NormalizeParticipants(salvagedParticipants), Degraded: true);
  }
  ```
  Note: `TrySalvage`'s third tuple element (`LocatedAnyArray`) does not need to be branched on
  here — when it is `false`, `TrySalvage` already returns empty `Tasks`/`Participants` lists,
  which naturally produces the FR-2(b) full-fallback result (`MeetingExtractionResult([], [],
  Degraded: true)`) through the same code path, with no special-casing required.

- [ ] **Step 4: Run the full extractor test file and confirm everything passes, including the
  pre-existing "not valid JSON at all" and happy-path tests (GREEN, no regressions).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"
  ```

- [ ] **Step 5: Build and format.**
  ```bash
  dotnet build
  dotnet format
  ```

- [ ] **Step 6: Commit.**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs \
          backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs
  git commit -m "Wire partial-extraction salvage into ClaudeMeetingTaskExtractor's JSON failure path"
  ```

---
