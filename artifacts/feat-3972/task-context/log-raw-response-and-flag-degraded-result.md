### task: log-raw-response-and-flag-degraded-result

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs` (line 10)
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs` (lines 76-80)
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs`

Reference files read to produce this task (do not modify):
- `ClaudeMeetingTaskExtractor.cs` — confirmed the current catch block (lines 76-80):
  ```csharp
  catch (JsonException ex)
  {
      _logger.LogError(ex, "Meeting task extraction returned malformed JSON — transcript will be imported without tasks");
      return new MeetingExtractionResult([], []);
  }
  ```
  and that `text` (line 61, `var text = StripMarkdownCodeFence(response.Text ?? string.Empty);`) is
  the post-fence-stripped, pre-deserialization string already in scope inside this catch block.
- `ClaudeMeetingTaskExtractorTests.cs` — confirmed the existing test
  `ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty` (lines 145-167) feeds the chat response
  `"not-valid-json{{{"` and verifies a `LogLevel.Error` call whose formatted state contains
  `"malformed JSON"`, and `ExtractAsync_WithValidJsonResponse_ReturnsParsedTasks` (lines 42-53) is
  the happy-path test.

Steps:

- [ ] **Step 1: Add the `Degraded` field to `MeetingExtractionResult` (plumbing, no dedicated test).**
  This is a source-compatible record-shape addition with a default value — there is no meaningful
  behavior to test-first here, so this step skips TDD by design (per the plan's TDD-skip allowance
  for pure plumbing). Edit `IMeetingTaskExtractor.cs` line 10 from:
  ```csharp
  public record MeetingExtractionResult(List<ExtractedTask> Tasks, List<string> Participants);
  ```
  to:
  ```csharp
  public record MeetingExtractionResult(List<ExtractedTask> Tasks, List<string> Participants, bool Degraded = false);
  ```

- [ ] **Step 2: Write a failing test asserting the raw response is logged as a structured property.**
  Add to `ClaudeMeetingTaskExtractorTests.cs` (after `ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty`):
  ```csharp
  [Fact]
  public async Task ExtractAsync_WhenJsonInvalid_LogsRawResponseAsStructuredProperty()
  {
      const string malformedJson = "not-valid-json{{{";
      _mockChatClient
          .Setup(x => x.GetResponseAsync(
              It.IsAny<IEnumerable<ChatMessage>>(),
              It.IsAny<ChatOptions?>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, malformedJson)]));

      await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

      _mockLogger.Verify(
          x => x.Log(
              LogLevel.Error,
              It.IsAny<EventId>(),
              It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(malformedJson)),
              It.IsAny<Exception>(),
              It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
          Times.Once);
  }
  ```

- [ ] **Step 3: Run the tests and confirm the new test fails (RED).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests.ExtractAsync_WhenJsonInvalid_LogsRawResponseAsStructuredProperty"
  ```
  Expect failure: the current log message has no `{RawResponse}` property, so the formatted state
  does not contain `malformedJson`.

- [ ] **Step 4: Implement — add the structured `{RawResponse}` property and set `Degraded: true`.**
  Edit the catch block in `ClaudeMeetingTaskExtractor.cs` (lines 76-80) from:
  ```csharp
  catch (JsonException ex)
  {
      _logger.LogError(ex, "Meeting task extraction returned malformed JSON — transcript will be imported without tasks");
      return new MeetingExtractionResult([], []);
  }
  ```
  to:
  ```csharp
  catch (JsonException ex)
  {
      _logger.LogError(ex, "Meeting task extraction returned malformed JSON — transcript will be imported without tasks. Raw response: {RawResponse}", text);
      return new MeetingExtractionResult([], [], Degraded: true);
  }
  ```
  This is a full-text, non-truncated structured property (`text` is bounded by `MaxOutputTokens =
  8192`, per NFR-1/FR-1's acceptance criteria) and `ex` is still passed to the logger so
  `JsonException.Path`/`LineNumber`/`BytePositionInLine` remain in the log record. The happy-path
  return (line 74, `return new MeetingExtractionResult(tasks, participants);`) is left untouched —
  `Degraded` defaults to `false` there, satisfying NFR-1 (no behavior/latency change on the success
  path).

- [ ] **Step 5: Update the two existing tests that now need new/adjusted assertions.**
  In `ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty`, add after the existing `Participants`
  assertion:
  ```csharp
  result.Degraded.Should().BeTrue();
  ```
  In `ExtractAsync_WithValidJsonResponse_ReturnsParsedTasks`, add after the existing
  `AssigneeEmail` assertion:
  ```csharp
  result.Degraded.Should().BeFalse();
  ```

- [ ] **Step 6: Run the full test file and confirm all tests pass (GREEN).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"
  ```

- [ ] **Step 7: Build and format.**
  ```bash
  dotnet build
  dotnet format
  ```

- [ ] **Step 8: Commit.**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExtractor.cs \
          backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs \
          backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs
  git commit -m "Log raw response and flag degraded result on meeting extraction JSON parse failure"
  ```

---
