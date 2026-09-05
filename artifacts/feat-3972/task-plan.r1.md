# Plan: Fix silent data loss on malformed LLM JSON in meeting task extraction

## Overview
`ClaudeMeetingTaskExtractor.ExtractAsync` currently swallows any `JsonException` from Claude's
meeting-extraction response, logging only the exception and silently discarding every task and
participant. This plan makes the failure diagnosable (log the raw response), less destructive (a
depth-aware raw-text scanner salvages whatever individual tasks/participants can still be parsed),
and visible (a new `TasksExtractionDegraded` flag flows from the extractor through the domain
entity, a manual EF Core migration, the `MeetingTranscriptDto` contract, and into two React pages
as an amber warning banner/pill). Tech stack: .NET 8 / MediatR / EF Core (Npgsql) on the backend,
React + TanStack Query + Tailwind on the frontend, xUnit + Moq + FluentAssertions and Jest +
Testing Library for tests — all within the existing `MeetingTasks` vertical slice, no new modules.

Per the architecture review, FR-2's acceptance-criteria wording naming `JsonDocument.Parse` in a
"permissive mode" is a documented spec deviation: no such mode exists in `System.Text.Json` (it
would throw at the identical position `JsonSerializer.Deserialize` already failed at). This plan
implements the review's approved replacement instead: a custom depth-aware raw-text scanner
(`PartialExtractionParser`) that locates the `tasks`/`participants` array bodies via manual
bracket/quote/escape tracking (never itself requiring the scanned bytes to be valid JSON), splits
each into element substrings, and deserializes each independently via the existing
`JsonSerializer.Deserialize<ExtractedTask>` — dropping and logging only the individual malformed
element(s). The acceptance criteria themselves (skip-and-log per element, preserve order,
`Degraded` flag, three-tier fallback) are unchanged.

---

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

### task: add-partial-extraction-parser-primitives

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PartialExtractionParser.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PartialExtractionParserTests.cs`

Reference files read to produce this task (do not modify):
- `IMeetingTaskExtractor.cs` — confirmed `ExtractedTask` shape:
  `public record ExtractedTask(string Title, string Description, string Assignee, DateTime?
  DueDate, string? AssigneeEmail = null);` (used by `TrySalvage` to deserialize each recovered
  task element with the same type as the happy path).
- `ClaudeMeetingTaskExtractor.cs` — confirmed the `JsonSerializerOptions` used elsewhere in this
  slice (`new() { PropertyNameCaseInsensitive = true }`), reused here so salvaged elements
  deserialize with the same case-insensitivity as the happy path.
- `Anela.Heblo.Application.csproj` line 48 / `AssemblyInfo.cs` line 3 — confirmed
  `[InternalsVisibleTo("Anela.Heblo.Tests")]` already exists, so the `internal static` primitives
  below are directly testable from `Anela.Heblo.Tests` without making them `public`.

Steps:

- [ ] **Step 1: Write failing tests for `FindTopLevelArrayBody`.**
  Create `PartialExtractionParserTests.cs`:
  ```csharp
  using Anela.Heblo.Application.Features.MeetingTasks.Services;
  using FluentAssertions;
  using Microsoft.Extensions.Logging;
  using Moq;

  namespace Anela.Heblo.Tests.Features.MeetingTasks;

  public sealed class PartialExtractionParserTests
  {
      private readonly Mock<ILogger> _mockLogger = new();

      [Fact]
      public void FindTopLevelArrayBody_LocatesSimpleArray()
      {
          const string json = """{"tasks":[{"title":"A"}],"participants":["X"]}""";

          var body = PartialExtractionParser.FindTopLevelArrayBody(json, "tasks");

          body.Should().Be("""{"title":"A"}""");
      }

      [Fact]
      public void FindTopLevelArrayBody_ReturnsNull_WhenKeyMissing()
      {
          const string json = """{"foo":[1,2,3]}""";

          var body = PartialExtractionParser.FindTopLevelArrayBody(json, "tasks");

          body.Should().BeNull();
      }

      [Fact]
      public void FindTopLevelArrayBody_ReturnsNull_WhenValueIsNotAnArray()
      {
          const string json = """{"tasks":"not-an-array"}""";

          var body = PartialExtractionParser.FindTopLevelArrayBody(json, "tasks");

          body.Should().BeNull();
      }

      [Fact]
      public void FindTopLevelArrayBody_IgnoresBracesAndCommasInsideStringValues()
      {
          const string json = """{"tasks":[{"title":"Say \"hi\"","description":"contains { brace and } too"}]}""";

          var body = PartialExtractionParser.FindTopLevelArrayBody(json, "tasks");

          body.Should().Be("""{"title":"Say \"hi\"","description":"contains { brace and } too"}""");
      }

      [Fact]
      public void FindTopLevelArrayBody_FallsBackToEndOfText_WhenArrayIsTruncated()
      {
          const string json = """{"tasks":[{"title":"Good"},{"title":"Trunc""";

          var body = PartialExtractionParser.FindTopLevelArrayBody(json, "tasks");

          body.Should().Be("""{"title":"Good"},{"title":"Trunc""");
      }
  }
  ```

- [ ] **Step 2: Run the tests and confirm they fail to compile/run (RED — `PartialExtractionParser` does not exist yet).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PartialExtractionParserTests"
  ```

- [ ] **Step 3: Implement `FindTopLevelArrayBody` and its private helpers.**
  Create `PartialExtractionParser.cs`:
  ```csharp
  using System.Text.Json;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

  /// <summary>
  /// Best-effort salvage of the "tasks" and "participants" arrays from a Claude JSON response
  /// that failed <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/>
  /// as a whole. Locates each array's raw text via manual bracket/quote/escape-aware scanning
  /// (which never itself requires the scanned bytes to be valid JSON), splits it into top-level
  /// element substrings, and deserializes each element independently — dropping and logging only
  /// the individual element(s) that still fail to parse.
  /// </summary>
  internal static class PartialExtractionParser
  {
      private static readonly JsonSerializerOptions JsonOptions =
          new() { PropertyNameCaseInsensitive = true };

      /// <summary>
      /// Locates the raw text of a top-level array property's value (e.g. "tasks": [ ... ]) by
      /// scanning <paramref name="text"/> character-by-character, tracking object/array nesting
      /// depth and in-string/escape state. Only structural characters ({ } [ ] " \) need to be
      /// well-formed for this to succeed — the bytes between them (the element content) do not.
      /// Returns the array body (the substring between the brackets, exclusive), or null if the
      /// property key or a matching '[' cannot be located at nesting depth 1 (i.e. directly on
      /// the top-level response object).
      /// </summary>
      internal static string? FindTopLevelArrayBody(string text, string propertyName)
      {
          var depth = 0;
          var i = 0;
          while (i < text.Length)
          {
              var c = text[i];

              if (c == '"')
              {
                  var stringEnd = FindStringEnd(text, i + 1);
                  if (stringEnd < 0)
                      return null;

                  if (depth == 1)
                  {
                      var key = text.Substring(i + 1, stringEnd - i - 1);
                      var afterKey = SkipWhitespace(text, stringEnd + 1);
                      if (key == propertyName && afterKey < text.Length && text[afterKey] == ':')
                      {
                          var valueStart = SkipWhitespace(text, afterKey + 1);
                          if (valueStart < text.Length && text[valueStart] == '[')
                          {
                              var arrayEnd = FindMatchingBracket(text, valueStart);
                              return arrayEnd < 0
                                  ? text[(valueStart + 1)..]
                                  : text.Substring(valueStart + 1, arrayEnd - valueStart - 1);
                          }
                          return null;
                      }
                  }

                  i = stringEnd + 1;
                  continue;
              }

              if (c == '{' || c == '[')
                  depth++;
              else if (c == '}' || c == ']')
                  depth--;

              i++;
          }

          return null;
      }

      /// <summary>
      /// Given the index just after an opening '"', returns the index of the matching (unescaped)
      /// closing '"', or -1 if the string is unterminated.
      /// </summary>
      private static int FindStringEnd(string text, int contentStart)
      {
          var i = contentStart;
          while (i < text.Length)
          {
              if (text[i] == '\\')
              {
                  i += 2;
                  continue;
              }
              if (text[i] == '"')
                  return i;
              i++;
          }
          return -1;
      }

      private static int SkipWhitespace(string text, int start)
      {
          var i = start;
          while (i < text.Length && char.IsWhiteSpace(text[i]))
              i++;
          return i;
      }

      /// <summary>
      /// Given the index of an opening '[' or '{', returns the index of its matching closing
      /// bracket, skipping over any quoted strings encountered along the way (so a brace or
      /// bracket character inside a string value is never mistaken for a structural one).
      /// Returns -1 if the text ends before the bracket is closed (truncation).
      /// </summary>
      private static int FindMatchingBracket(string text, int openBracketIndex)
      {
          var depth = 0;
          var i = openBracketIndex;
          while (i < text.Length)
          {
              var c = text[i];
              if (c == '"')
              {
                  var stringEnd = FindStringEnd(text, i + 1);
                  if (stringEnd < 0)
                      return -1;
                  i = stringEnd + 1;
                  continue;
              }
              if (c == '[' || c == '{')
              {
                  depth++;
              }
              else if (c == ']' || c == '}')
              {
                  depth--;
                  if (depth == 0)
                      return i;
              }
              i++;
          }
          return -1;
      }
  }
  ```

- [ ] **Step 4: Run the tests and confirm the `FindTopLevelArrayBody` tests pass (GREEN).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PartialExtractionParserTests"
  ```

- [ ] **Step 5: Write failing tests for `SplitTopLevelElements`.**
  Add to `PartialExtractionParserTests.cs`:
  ```csharp
  [Fact]
  public void SplitTopLevelElements_ReturnsEmptyList_ForNullBody()
  {
      PartialExtractionParser.SplitTopLevelElements(null).Should().BeEmpty();
  }

  [Fact]
  public void SplitTopLevelElements_ReturnsEmptyList_ForEmptyOrWhitespaceBody()
  {
      PartialExtractionParser.SplitTopLevelElements("").Should().BeEmpty();
      PartialExtractionParser.SplitTopLevelElements("   ").Should().BeEmpty();
  }

  [Fact]
  public void SplitTopLevelElements_IgnoresCommasInsideNestedStringsAndObjects()
  {
      const string body = """{"title":"A, B","description":"x"},{"title":"C","description":"y, z"}""";

      var elements = PartialExtractionParser.SplitTopLevelElements(body);

      elements.Should().Equal(
          """{"title":"A, B","description":"x"}""",
          """{"title":"C","description":"y, z"}""");
  }

  [Fact]
  public void SplitTopLevelElements_ReturnsIncompleteTrailingElement_WhenTruncated()
  {
      const string body = """{"title":"Good"},{"title":"Trunc""";

      var elements = PartialExtractionParser.SplitTopLevelElements(body);

      elements.Should().Equal("""{"title":"Good"}""", """{"title":"Trunc""");
  }
  ```

- [ ] **Step 6: Run the tests and confirm they fail to compile (RED — `SplitTopLevelElements` does not exist yet).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PartialExtractionParserTests"
  ```

- [ ] **Step 7: Implement `SplitTopLevelElements`.**
  Add to `PartialExtractionParser.cs` (alongside the existing primitives):
  ```csharp
      /// <summary>
      /// Splits an array body (the text between its brackets) into individual element
      /// substrings at depth-1 commas, using the same bracket/quote/escape-aware scanning as
      /// <see cref="FindTopLevelArrayBody"/>. Returns an empty list for a null, empty, or
      /// whitespace-only body.
      /// </summary>
      internal static IReadOnlyList<string> SplitTopLevelElements(string? arrayBody)
      {
          if (string.IsNullOrWhiteSpace(arrayBody))
              return Array.Empty<string>();

          var elements = new List<string>();
          var depth = 0;
          var elementStart = 0;
          var i = 0;
          while (i < arrayBody.Length)
          {
              var c = arrayBody[i];

              if (c == '"')
              {
                  var stringEnd = FindStringEnd(arrayBody, i + 1);
                  i = stringEnd < 0 ? arrayBody.Length : stringEnd + 1;
                  continue;
              }

              if (c == '{' || c == '[')
              {
                  depth++;
              }
              else if (c == '}' || c == ']')
              {
                  depth--;
              }
              else if (c == ',' && depth == 0)
              {
                  AddElementIfNotBlank(elements, arrayBody, elementStart, i);
                  elementStart = i + 1;
              }

              i++;
          }

          AddElementIfNotBlank(elements, arrayBody, elementStart, arrayBody.Length);

          return elements;
      }

      private static void AddElementIfNotBlank(List<string> elements, string arrayBody, int start, int end)
      {
          if (start >= end)
              return;

          var slice = arrayBody[start..end].Trim();
          if (slice.Length > 0)
              elements.Add(slice);
      }
  ```

- [ ] **Step 8: Run the tests and confirm the `SplitTopLevelElements` tests pass (GREEN).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PartialExtractionParserTests"
  ```

- [ ] **Step 9: Write failing tests for the `TrySalvage` entry point.**
  Add to `PartialExtractionParserTests.cs`:
  ```csharp
  private const string ThreeTasksOneWithInvalidEscape = """
      {"participants":["Alice","Bob"],"tasks":[
        {"title":"Good Task","description":"fine","assignee":"Alice","assigneeEmail":null,"dueDate":null},
        {"title":"Bad\-Task","description":"broken escape","assignee":"Bob","assigneeEmail":null,"dueDate":null},
        {"title":"Another Good","description":"ok","assignee":"Alice","assigneeEmail":null,"dueDate":null}
      ]}
      """;

  [Fact]
  public void TrySalvage_DropsOnlyTheMalformedElement_KeepsOthersInOrder()
  {
      var (tasks, participants, locatedAnyArray) =
          PartialExtractionParser.TrySalvage(ThreeTasksOneWithInvalidEscape, _mockLogger.Object);

      locatedAnyArray.Should().BeTrue();
      tasks.Should().HaveCount(2);
      tasks[0].Title.Should().Be("Good Task");
      tasks[1].Title.Should().Be("Another Good");
      participants.Should().Equal("Alice", "Bob");
  }

  [Fact]
  public void TrySalvage_LogsOneWarningForTheDroppedElement()
  {
      PartialExtractionParser.TrySalvage(ThreeTasksOneWithInvalidEscape, _mockLogger.Object);

      _mockLogger.Verify(
          x => x.Log(
              LogLevel.Warning,
              It.IsAny<EventId>(),
              It.IsAny<It.IsAnyType>(),
              It.IsAny<Exception>(),
              It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
          Times.Once);
  }

  [Fact]
  public void TrySalvage_ReturnsLocatedAnyArrayFalse_WhenTextIsNotJsonShaped()
  {
      var (tasks, participants, locatedAnyArray) =
          PartialExtractionParser.TrySalvage("This is not json at all, sorry.", _mockLogger.Object);

      locatedAnyArray.Should().BeFalse();
      tasks.Should().BeEmpty();
      participants.Should().BeEmpty();
  }

  [Fact]
  public void TrySalvage_WithFullyValidInput_ReturnsAllElementsAndLogsNoWarnings()
  {
      const string valid = """{"participants":["X"],"tasks":[{"title":"T","description":"D","assignee":"X","assigneeEmail":null,"dueDate":null}]}""";

      var (tasks, participants, locatedAnyArray) =
          PartialExtractionParser.TrySalvage(valid, _mockLogger.Object);

      locatedAnyArray.Should().BeTrue();
      tasks.Should().HaveCount(1);
      participants.Should().Equal("X");
      _mockLogger.Verify(
          x => x.Log(
              LogLevel.Warning,
              It.IsAny<EventId>(),
              It.IsAny<It.IsAnyType>(),
              It.IsAny<Exception>(),
              It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
          Times.Never);
  }
  ```

- [ ] **Step 10: Run the tests and confirm they fail to compile (RED — `TrySalvage` does not exist yet).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PartialExtractionParserTests"
  ```

- [ ] **Step 11: Implement `TrySalvage`.**
  Add to `PartialExtractionParser.cs` (as the public entry point, above the primitives):
  ```csharp
      internal static (List<ExtractedTask> Tasks, List<string> Participants, bool LocatedAnyArray) TrySalvage(
          string text, ILogger logger)
      {
          var tasksBody = FindTopLevelArrayBody(text, "tasks");
          var participantsBody = FindTopLevelArrayBody(text, "participants");

          if (tasksBody is null && participantsBody is null)
          {
              return (new List<ExtractedTask>(), new List<string>(), false);
          }

          var tasks = new List<ExtractedTask>();
          var taskElements = SplitTopLevelElements(tasksBody);
          for (var i = 0; i < taskElements.Count; i++)
          {
              try
              {
                  var task = JsonSerializer.Deserialize<ExtractedTask>(taskElements[i], JsonOptions);
                  if (task is not null)
                      tasks.Add(task);
              }
              catch (JsonException ex)
              {
                  logger.LogWarning(ex, "Dropping malformed task at index {Index}: {RawElement}", i, taskElements[i]);
              }
          }

          var participants = new List<string>();
          var participantElements = SplitTopLevelElements(participantsBody);
          for (var i = 0; i < participantElements.Count; i++)
          {
              try
              {
                  var participant = JsonSerializer.Deserialize<string>(participantElements[i], JsonOptions);
                  if (participant is not null)
                      participants.Add(participant);
              }
              catch (JsonException ex)
              {
                  logger.LogWarning(ex, "Dropping malformed participant at index {Index}: {RawElement}", i, participantElements[i]);
              }
          }

          return (tasks, participants, true);
      }
  ```
  Add `using System;` and `using System.Collections.Generic;` to the top of the file alongside the
  existing `using System.Text.Json;` / `using Microsoft.Extensions.Logging;` (needed for
  `Array.Empty<string>()`, `List<T>`, `IReadOnlyList<T>`).

- [ ] **Step 12: Run the full test file and confirm all tests pass (GREEN).**
  ```bash
  dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PartialExtractionParserTests"
  ```

- [ ] **Step 13: Build and format.**
  ```bash
  dotnet build
  dotnet format
  ```

- [ ] **Step 14: Commit.**
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PartialExtractionParser.cs \
          backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PartialExtractionParserTests.cs
  git commit -m "Add depth-aware partial-extraction parser primitives for malformed meeting JSON"
  ```

---

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

### task: add-tasksextractiondegraded-domain-and-migration

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs` (line 16)
- Modify: `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs` (after line 68)
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/{timestamp}_AddTasksExtractionDegraded.cs`
  (and its auto-generated `.Designer.cs`, plus an update to `ApplicationDbContextModelSnapshot.cs`)

Reference files read to produce this task (do not modify):
- `MeetingTranscript.cs` — confirmed current shape (`Participants` list property at line 16,
  `AccessLevel` at line 17).
- `MeetingTranscriptConfiguration.cs` — confirmed the `Participants` property configuration ends
  at line 68 (`.Metadata.SetValueComparer(ParticipantsComparer);`) and `AccessLevel`'s
  configuration begins at line 70 with `.HasDefaultValue(MeetingAccessLevel.Private)` as the
  precedent for declaring a Fluent-API default.
- `20260714103910_AddMeetingParticipants.cs` — precedent for a single-column migration on this
  same `MeetingTranscripts` table (`jsonb`/`string` shape, not directly reusable for a bool but
  confirms the file/namespace/`#nullable disable` boilerplate).
- `20250901084258_AddInvoiceAcquiredToPurchaseOrder.cs` — exact precedent for a single
  `bool` column addition:
  ```csharp
  migrationBuilder.AddColumn<bool>(
      name: "InvoiceAcquired",
      schema: "public",
      table: "PurchaseOrders",
      type: "boolean",
      nullable: false,
      defaultValue: false);
  ```
- `ApplicationDbContextModelSnapshot.cs` lines 2866-2935 — confirmed the exact current
  `MeetingTranscript` entity block (property list order: `Id`, `AccessLevel`, `Participants`,
  `PlaudCreatedAt`, `PlaudRecordingId`, `RawTranscript`, `ReceivedAt`, `ReviewedAt`,
  `ReviewedByUser`, `Status`, `Subject`, `Summary`, then `HasKey`/indexes/`ToTable`) and, at lines
  3554-3555, confirmed that a non-nullable `bool` property with no Fluent-API default renders in
  the snapshot as `b.Property<bool>("InvoiceAcquired").HasColumnType("boolean");` (no
  `.IsRequired()`, since value types are non-nullable by default) — whereas `AccessLevel` (which
  *does* declare `.HasDefaultValue(...)` in its configuration) renders with
  `.ValueGeneratedOnAdd()` and `.HasDefaultValue(...)` in the snapshot. Since this task's new
  property *will* declare `.HasDefaultValue(false)`, the auto-generated snapshot entry is expected
  to be:
  ```csharp
  b.Property<bool>("TasksExtractionDegraded")
      .ValueGeneratedOnAdd()
      .HasColumnType("boolean")
      .HasDefaultValue(false);
  ```
  inserted alphabetically after `Summary` (line 2917) and before `HasKey("Id")` (line 2919).
- `ls backend/src/Anela.Heblo.Persistence/Migrations` — confirmed the latest existing migration
  timestamp is `20260810105649` (`AddOvertimeLedger`), so the new migration's EF-tool-generated
  timestamp (today, 2026-08-29) sorts after it with no manual timestamp collision risk.

This is a schema-migration task — there is no meaningful unit test for an EF Core migration file
itself (it is verified by generating it against the updated entity/configuration and diffing the
result against the expected shape below), so this task explicitly skips TDD.

Steps:

- [ ] **Step 1: Add the property to the domain entity.**
  Edit `MeetingTranscript.cs` — insert after line 16 (`public List<string> Participants { get; set; } = new();`):
  ```csharp
      public bool TasksExtractionDegraded { get; set; }
  ```

- [ ] **Step 2: Add the Fluent API configuration.**
  Edit `MeetingTranscriptConfiguration.cs` — insert after line 68
  (`.Metadata.SetValueComparer(ParticipantsComparer);`) and before line 70
  (`builder.Property(x => x.AccessLevel)`):
  ```csharp
          builder.Property(x => x.TasksExtractionDegraded)
              .IsRequired()
              .HasDefaultValue(false);
  ```

- [ ] **Step 3: Generate the migration via the EF Core CLI.**
  ```bash
  dotnet ef migrations add AddTasksExtractionDegraded --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
  ```
  This scaffolds `{timestamp}_AddTasksExtractionDegraded.cs`, its `.Designer.cs`, and updates
  `ApplicationDbContextModelSnapshot.cs` automatically.

- [ ] **Step 4: Verify the generated migration's `Up`/`Down` exactly matches the expected shape.**
  Open the newly generated `{timestamp}_AddTasksExtractionDegraded.cs` and confirm it reads:
  ```csharp
  using Microsoft.EntityFrameworkCore.Migrations;

  #nullable disable

  namespace Anela.Heblo.Persistence.Migrations
  {
      /// <inheritdoc />
      public partial class AddTasksExtractionDegraded : Migration
      {
          /// <inheritdoc />
          protected override void Up(MigrationBuilder migrationBuilder)
          {
              migrationBuilder.AddColumn<bool>(
                  name: "TasksExtractionDegraded",
                  schema: "public",
                  table: "MeetingTranscripts",
                  type: "boolean",
                  nullable: false,
                  defaultValue: false);
          }

          /// <inheritdoc />
          protected override void Down(MigrationBuilder migrationBuilder)
          {
              migrationBuilder.DropColumn(
                  name: "TasksExtractionDegraded",
                  schema: "public",
                  table: "MeetingTranscripts");
          }
      }
  }
  ```
  If the tool produces a different shape (e.g. missing `defaultValue: false`, or a different
  `schema`/`table`), hand-correct the file to match the above exactly before proceeding — this is
  the contractually expected shape per the `AddInvoiceAcquiredToPurchaseOrder` precedent and the
  Data Model section of the spec (`nullable: false, defaultValue: false`, no backfill of existing
  rows beyond the column default).

- [ ] **Step 5: Verify the model snapshot update.**
  In `ApplicationDbContextModelSnapshot.cs`, confirm a new property block was inserted into the
  `MeetingTranscript` entity (alphabetically after `Summary`, before `HasKey("Id")`):
  ```csharp
                      b.Property<bool>("TasksExtractionDegraded")
                          .ValueGeneratedOnAdd()
                          .HasColumnType("boolean")
                          .HasDefaultValue(false);
  ```

- [ ] **Step 6: Build.**
  ```bash
  dotnet build
  ```

- [ ] **Step 7: Apply the migration to the local database (manual step per project convention —
  not part of automated deployment; staging/production application happens separately after this
  PR merges).**
  ```bash
  dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
  ```

- [ ] **Step 8: Format.**
  ```bash
  dotnet format
  ```

- [ ] **Step 9: Commit.**
  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/MeetingTasks/MeetingTranscript.cs \
          backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptConfiguration.cs \
          backend/src/Anela.Heblo.Persistence/Migrations/
  git commit -m "Add TasksExtractionDegraded column to MeetingTranscripts"
  ```

---

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

### task: regenerate-openapi-client-and-update-meeting-tasks-hook

**Files:**
- Modify (auto-regenerated): `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/api/hooks/useMeetingTasks.ts` (line 44, inside the `MeetingTranscriptDto` interface)

Reference files read to produce this task (do not modify):
- `useMeetingTasks.ts` — confirmed this file hand-rolls its **own** `MeetingTranscriptDto`
  TypeScript interface (lines 28-46) and fetches via a local `fetchJson` helper (lines 114-122)
  built on `getAuthenticatedApiClient()` + `${(apiClient as any).baseUrl}${path}` — it never
  imports from the generated `api-client.ts`, despite the stale `// TODO: migrate to generated
  client...` comment at line 1. This means regenerating the OpenAPI client alone does **not**
  surface the new field to either page — this task's manual interface edit is required in
  addition, not instead of, regeneration.
- `docs/development/api-client-generation.md` — confirmed `npm run generate-client` is the
  regeneration command, and that it also runs automatically as a `prebuild` step before `npm run
  build`.

This is a plumbing task (a generated-file regeneration plus a one-field interface addition) with
no meaningful unit to test-first — TDD is explicitly skipped here; correctness is verified by the
frontend build's type-checking in the next two tasks, which will fail to compile if this field is
missing when the page components reference it.

Steps:

- [ ] **Step 1: Regenerate the OpenAPI TypeScript client** (now that the backend `MeetingTranscriptDto`
  carries `TasksExtractionDegraded`, from the previous task):
  ```bash
  cd frontend
  npm run generate-client
  ```
  This updates the generated `MeetingTranscriptDto` class in `api-client.ts` for consistency (it
  is not directly consumed by `useMeetingTasks.ts`, but keeping it in sync avoids future drift for
  any consumer that does use the generated client).

- [ ] **Step 2: Add the field to the hand-written interface.**
  Edit `useMeetingTasks.ts` — insert after line 44 (`accessLevel: 'Private' | 'Public' | 'Restricted';`)
  and before line 45 (`accessGrants: MeetingAccessGrantDto[];`):
  ```typescript
    accessLevel: 'Private' | 'Public' | 'Restricted';
    accessGrants: MeetingAccessGrantDto[];
    tasksExtractionDegraded: boolean;
  ```
  (Field ordering here is not significant — appending after `accessGrants` and before the closing
  `}` of the interface, at what is currently line 46, is equally correct; keep it adjacent to the
  other transcript-level flags for readability.)

- [ ] **Step 3: Commit.**
  ```bash
  git add frontend/src/api/generated/api-client.ts frontend/src/api/hooks/useMeetingTasks.ts
  git commit -m "Expose tasksExtractionDegraded on the meeting transcript DTO/client"
  ```

---

### task: add-degraded-warning-banner-to-detail-page

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` (insert after line 393)
- Create: `frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.degraded.test.tsx`

Reference files read to produce this task (do not modify):
- `MeetingTaskDetailPage.tsx` — confirmed `AlertTriangle` is already imported at line 7; confirmed
  the header block (holding `TranscriptStatusBadge`) closes with `</div>` at line 393, immediately
  followed by the `{reimportError && (...)}` block at lines 395-399; confirmed the existing
  "neznámý uživatel" amber-pill idiom at lines 579-583 (`text-amber-700 bg-amber-100
  dark:text-amber-300 dark:bg-amber-900/30` + `AlertTriangle`) as the color/icon precedent to
  reuse verbatim, per the design doc.
- `MeetingTaskDetailPage.reviewState.test.tsx` — confirmed the full test harness pattern (module
  mocks for `react-markdown`, `remark-gfm`, `useMeetingTasks` hooks, `PermissionsContext`,
  `useAuth`, `explain/*`, `access/ManageAccessModal`; a `buildTranscript()`/`setupHooks()`/`renderPage()`
  helper trio) that this new test file will replicate, per the existing per-concern convention
  (`.filter.`, `.download.`, `.delete.`, `.reviewState.` test files each keep their own copy of
  this harness).

Steps:

- [ ] **Step 1: Write a failing test file asserting the banner's presence/absence.**
  Create `MeetingTaskDetailPage.degraded.test.tsx`:
  ```tsx
  import React from 'react';
  import { render, screen } from '@testing-library/react';
  import { MemoryRouter, Route, Routes } from 'react-router-dom';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

  import {
    useMeetingTaskDetail,
    useUpdateProposedTask,
    useUpdateProposedTaskStatus,
    useUpdateTranscriptStatus,
    useAddProposedTask,
    useSubmitToTodo,
    useMeetingUsers,
    useReimportMeeting,
    useExplainMeetingSummary,
    useDeleteMeeting,
  } from '../../../../api/hooks/useMeetingTasks';
  import { useExplainSelection } from '../explain/useExplainSelection';
  import MeetingTaskDetailPage from '../MeetingTaskDetailPage';

  // ---- Module mocks ----

  jest.mock('react-markdown', () => ({ __esModule: true, default: ({ children }: { children: React.ReactNode }) => <>{children}</> }));
  jest.mock('remark-gfm', () => ({ __esModule: true, default: () => {} }));

  jest.mock('../../../../api/hooks/useMeetingTasks');
  jest.mock('../../../../auth/PermissionsContext', () => ({
    usePermissionsContext: () => ({
      permissions: [],
      isSuperUser: true,
      groups: [],
      isLoading: false,
      hasPermission: () => true,
    }),
  }));
  jest.mock('../../../../auth/useAuth', () => ({
    useAuth: () => ({ account: { username: 'me@anela.cz' } }),
  }));
  jest.mock('../explain/useExplainSelection');
  jest.mock('../explain/ExplainTooltip', () => ({ ExplainTooltip: () => null }));
  jest.mock('../explain/ExplainModal', () => ({ ExplainModal: () => null }));
  jest.mock('../access/ManageAccessModal', () => ({ ManageAccessModal: () => null }));

  // ---- Helpers ----

  const noopMutation = { mutate: jest.fn(), mutateAsync: jest.fn(), isPending: false, isError: false, error: null, reset: jest.fn() };

  function buildTranscript(overrides: Record<string, unknown> = {}) {
    return {
      id: 'abc',
      subject: 'Schůzka',
      summary: 'AI summary text',
      rawTranscript: 'Speaker: Hello',
      plaudRecordingId: 'plaud-1',
      plaudCreatedAt: '2026-05-19T10:00:00Z',
      status: 'PendingReview',
      receivedAt: '2026-05-19T10:00:00Z',
      reviewedAt: null,
      reviewedByUser: null,
      taskCount: 0,
      approvedTaskCount: 0,
      rejectedTaskCount: 0,
      tasks: [],
      participants: [],
      accessLevel: 'Private' as const,
      accessGrants: [],
      tasksExtractionDegraded: false,
      ...overrides,
    };
  }

  function renderPage() {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/automation/meeting-tasks/abc']}>
          <Routes>
            <Route path="/automation/meeting-tasks/:id" element={<MeetingTaskDetailPage />} />
            <Route path="/automation/meeting-tasks" element={<div>SEZNAM PORAD</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );
  }

  function setupHooks(transcriptOverrides: Record<string, unknown> = {}) {
    (useMeetingTaskDetail as jest.Mock).mockReturnValue({ isLoading: false, data: { transcript: buildTranscript(transcriptOverrides) } });
    (useUpdateProposedTask as jest.Mock).mockReturnValue(noopMutation);
    (useUpdateProposedTaskStatus as jest.Mock).mockReturnValue(noopMutation);
    (useUpdateTranscriptStatus as jest.Mock).mockReturnValue(noopMutation);
    (useAddProposedTask as jest.Mock).mockReturnValue(noopMutation);
    (useSubmitToTodo as jest.Mock).mockReturnValue(noopMutation);
    (useMeetingUsers as jest.Mock).mockReturnValue({ data: [] });
    (useReimportMeeting as jest.Mock).mockReturnValue(noopMutation);
    (useExplainMeetingSummary as jest.Mock).mockReturnValue(noopMutation);
    (useDeleteMeeting as jest.Mock).mockReturnValue(noopMutation);
    (useExplainSelection as jest.Mock).mockReturnValue({ selectedText: null, clearSelection: jest.fn() });
  }

  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('extraction-degraded warning banner', () => {
    it('renders a warning banner when tasksExtractionDegraded is true', () => {
      setupHooks({ tasksExtractionDegraded: true });
      renderPage();
      expect(screen.getByText(/Extrakce úkolů může být neúplná/i)).toBeInTheDocument();
    });

    it('renders no banner when tasksExtractionDegraded is false', () => {
      setupHooks({ tasksExtractionDegraded: false });
      renderPage();
      expect(screen.queryByText(/Extrakce úkolů může být neúplná/i)).not.toBeInTheDocument();
    });
  });
  ```

- [ ] **Step 2: Run the test and confirm it fails (RED — the page renders no such banner yet).**
  ```bash
  cd frontend
  npx react-scripts test --watchAll=false MeetingTaskDetailPage.degraded
  ```

- [ ] **Step 3: Implement the banner.**
  Edit `MeetingTaskDetailPage.tsx` — insert immediately after line 393 (the header row's closing
  `</div>`) and before line 395 (`{reimportError && (`):
  ```tsx
        </div>
      </div>

      {transcript.tasksExtractionDegraded && (
        <div className="px-4 sm:px-6 lg:px-8 mt-2">
          <div className="flex items-start gap-2 rounded-md border border-amber-200 dark:border-amber-900/40 bg-amber-100 dark:bg-amber-900/30 px-3 py-2 text-sm text-amber-700 dark:text-amber-300">
            <AlertTriangle className="w-4 h-4 mt-0.5 shrink-0" aria-hidden="true" />
            <span>
              Extrakce úkolů může být neúplná — nepodařilo se zpracovat celou odpověď AI.
              Zkontrolujte přepis ručně, nebo použijte tlačítko "Reimport" výše.
            </span>
          </div>
        </div>
      )}

      {reimportError && (
  ```
  (The first two lines above, `</div>` / `</div>`, are the existing lines 392-393 shown for
  placement context — do not duplicate them; only the new `{transcript.tasksExtractionDegraded &&
  (...)}` block is new content, inserted between the existing line 393 and line 395.)

- [ ] **Step 4: Run the test and confirm it passes (GREEN).**
  ```bash
  npx react-scripts test --watchAll=false MeetingTaskDetailPage.degraded
  ```

- [ ] **Step 5: Run the full existing detail-page test suite to confirm no regressions.**
  ```bash
  npx react-scripts test --watchAll=false MeetingTaskDetailPage
  ```

- [ ] **Step 6: Build and lint.**
  ```bash
  npm run build
  npm run lint
  ```

- [ ] **Step 7: Commit.**
  ```bash
  git add frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx \
          frontend/src/components/pages/automation/__tests__/MeetingTaskDetailPage.degraded.test.tsx
  git commit -m "Show a warning banner on the meeting detail page when task extraction is degraded"
  ```

---

### task: add-degraded-indicator-to-list-page

**Files:**
- Modify: `frontend/src/components/pages/automation/MeetingTasksPage.tsx` (line 3 imports, and the "Ulohy" `<td>` at lines 191-198)
- Create: `frontend/src/components/pages/automation/__tests__/MeetingTasksPage.test.tsx`

Reference files read to produce this task (do not modify):
- `MeetingTasksPage.tsx` — confirmed the current `lucide-react` import (line 3) does **not**
  include `AlertTriangle` (unlike the detail page); confirmed the "Ulohy" `<td>` (lines 191-198)
  currently renders `{row.taskCount}` plus an optional `({row.approvedTaskCount} schvaleno)` span;
  confirmed the page's only hooks are `useNavigate` (react-router), `useScreenView` (telemetry),
  and `useMeetingTasksList` — no existing test file for this page exists yet (verified via file
  search), so this task creates the first one.
- `MeetingTaskDetailPage.reviewState.test.tsx` (already read for the previous task) and
  `JournalList.test.tsx` line 21-22 — confirmed the `useScreenView` mock pattern
  (`jest.mock('.../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }))`) needed since
  this page calls it directly (unlike the detail page, which doesn't).

Steps:

- [ ] **Step 1: Write a failing test file asserting the row-level pill's presence/absence.**
  Create `MeetingTasksPage.test.tsx`:
  ```tsx
  import React from 'react';
  import { render, screen } from '@testing-library/react';
  import { MemoryRouter } from 'react-router-dom';
  import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

  import { useMeetingTasksList } from '../../../../api/hooks/useMeetingTasks';
  import MeetingTasksPage from '../MeetingTasksPage';

  jest.mock('../../../../api/hooks/useMeetingTasks');
  jest.mock('../../../../telemetry/useScreenView', () => ({
    useScreenView: jest.fn(),
  }));

  function buildRow(overrides: Record<string, unknown> = {}) {
    return {
      id: 'abc',
      subject: 'Schůzka',
      plaudRecordingId: 'plaud-1',
      plaudCreatedAt: '2026-05-19T10:00:00Z',
      status: 'PendingReview',
      receivedAt: '2026-05-19T10:00:00Z',
      taskCount: 3,
      approvedTaskCount: 0,
      rejectedTaskCount: 0,
      accessLevel: 'Private' as const,
      tasksExtractionDegraded: false,
      ...overrides,
    };
  }

  function mockList(rows: Record<string, unknown>[]) {
    (useMeetingTasksList as jest.Mock).mockReturnValue({
      data: { items: rows, totalCount: rows.length, pageNumber: 1, pageSize: 20, totalPages: 1 },
      isLoading: false,
      isFetching: false,
      refetch: jest.fn(),
    });
  }

  function renderPage() {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <MeetingTasksPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );
  }

  describe('extraction-degraded row indicator', () => {
    it('shows a warning pill for a row with tasksExtractionDegraded set', () => {
      mockList([buildRow({ tasksExtractionDegraded: true })]);
      renderPage();
      expect(screen.getByTitle('extrakce může být neúplná')).toBeInTheDocument();
    });

    it('shows no warning pill for a row without the flag', () => {
      mockList([buildRow({ tasksExtractionDegraded: false })]);
      renderPage();
      expect(screen.queryByTitle('extrakce může být neúplná')).not.toBeInTheDocument();
    });
  });
  ```

- [ ] **Step 2: Run the test and confirm it fails (RED — no such pill exists yet).**
  ```bash
  cd frontend
  npx react-scripts test --watchAll=false MeetingTasksPage.test
  ```

- [ ] **Step 3: Implement the row-level pill.**
  Edit `MeetingTasksPage.tsx` line 3 from:
  ```typescript
  import { Clock, CheckCircle, CheckCircle2, ChevronLeft, ChevronRight, RefreshCw } from "lucide-react";
  ```
  to:
  ```typescript
  import { Clock, CheckCircle, CheckCircle2, ChevronLeft, ChevronRight, RefreshCw, AlertTriangle } from "lucide-react";
  ```
  Edit the "Ulohy" `<td>` (lines 191-198) from:
  ```tsx
                  <td className="px-4 py-2 text-sm text-gray-700 dark:text-graphite-muted">
                    {row.taskCount}
                    {row.approvedTaskCount > 0 && (
                      <span className="ml-1 text-xs text-gray-500 dark:text-graphite-muted">
                        ({row.approvedTaskCount} schvaleno)
                      </span>
                    )}
                  </td>
  ```
  to:
  ```tsx
                  <td className="px-4 py-2 text-sm text-gray-700 dark:text-graphite-muted">
                    {row.taskCount}
                    {row.approvedTaskCount > 0 && (
                      <span className="ml-1 text-xs text-gray-500 dark:text-graphite-muted">
                        ({row.approvedTaskCount} schvaleno)
                      </span>
                    )}
                    {row.tasksExtractionDegraded && (
                      <span
                        title="extrakce může být neúplná"
                        className="ml-1 inline-flex items-center text-amber-700 bg-amber-100 dark:text-amber-300 dark:bg-amber-900/30 rounded-full px-1.5 py-0.5"
                      >
                        <AlertTriangle className="w-3 h-3" />
                      </span>
                    )}
                  </td>
  ```

- [ ] **Step 4: Run the test and confirm it passes (GREEN).**
  ```bash
  npx react-scripts test --watchAll=false MeetingTasksPage.test
  ```

- [ ] **Step 5: Build and lint.**
  ```bash
  npm run build
  npm run lint
  ```

- [ ] **Step 6: Commit.**
  ```bash
  git add frontend/src/components/pages/automation/MeetingTasksPage.tsx \
          frontend/src/components/pages/automation/__tests__/MeetingTasksPage.test.tsx
  git commit -m "Show a per-row warning pill on the meeting list page when task extraction is degraded"
  ```

---

## Self-review

- **FR-1 coverage:** `log-raw-response-and-flag-degraded-result` (structured `{RawResponse}`
  property, full untruncated text, `ex` still passed, dedicated unit test).
- **FR-2 coverage:** `add-partial-extraction-parser-primitives` (the scanner itself, with
  adversarial fixtures per the arch review's risk mitigation) and
  `wire-partial-recovery-into-extractor` (integration into the catch block; tests for (a) partial
  salvage with order preservation, (b) not-JSON-at-all fallback, (c) fully-valid `Degraded: false`
  — all three FR-2 acceptance-criteria scenarios are covered).
- **FR-3 coverage:** `add-tasksextractiondegraded-domain-and-migration` (entity + migration),
  `thread-degraded-flag-through-handlers-and-dto` (both handlers set it, both read handlers expose
  it, reimport overwrite-not-OR semantics tested in both directions),
  `regenerate-openapi-client-and-update-meeting-tasks-hook` (frontend type propagation, including
  the arch review's corrected understanding that the hand-written hook interface needs a manual
  edit), `add-degraded-warning-banner-to-detail-page` (unmissable banner near
  `TranscriptStatusBadge`, pointing at Reimport), `add-degraded-indicator-to-list-page` (row-level
  pill). No task filters out or hides degraded rows from review queues — FR-3's "informational
  only" constraint is respected by construction (no new filtering logic was added anywhere).
- **NFR-1 (no success-path cost):** `PartialExtractionParser.TrySalvage` is only ever invoked from
  inside the `catch (JsonException)` block, never on the success path; the happy-path return
  statement is untouched by any task.
- **NFR-2 (log sensitivity):** No redaction logic was added, matching the spec's explicit decision
  that no additional scrubbing is required.
- **Spec deviation flagged:** The FR-2 acceptance criteria's literal wording (`JsonDocument.Parse`
  in a "permissive mode") is explicitly called out as non-implementable and replaced with the
  arch-review-approved depth-aware scanner in both the Overview above and inline in the
  `add-partial-extraction-parser-primitives` task.
- **Type/method-name consistency check:** `MeetingExtractionResult(List<ExtractedTask> Tasks,
  List<string> Participants, bool Degraded = false)` is introduced once (first task) and used
  identically (including the named `Degraded:` argument) in every later task and test.
  `PartialExtractionParser.TrySalvage(string text, ILogger logger) -> (List<ExtractedTask> Tasks,
  List<string> Participants, bool LocatedAnyArray)` is introduced once (second task) and consumed
  identically in the third task. `TasksExtractionDegraded` (domain entity, DTO, and TypeScript
  interface) and `tasksExtractionDegraded` (TypeScript/JSON casing) are spelled consistently
  across every backend and frontend task. `ExtractedTask` and `NormalizeParticipants` are reused
  from the existing class rather than redefined.
- **Placeholder-language scan:** no "TBD", "similar to Task N", or undefined types/methods remain
  in any step; every code block above is complete and self-contained given the preceding tasks'
  changes.
