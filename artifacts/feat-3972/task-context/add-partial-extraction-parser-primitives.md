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
