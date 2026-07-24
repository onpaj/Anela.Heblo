### task: parse-summary-json-tests


## Goal
Add unit test coverage for the previously-untested static parser `PlaudCliClient.ParseSummaryJson(string json)`, which currently has zero dedicated tests despite its sibling parsers (`ParseFilesOutput`, `ParseFileDetail`) being fully covered. These tests exercise every branch of the parser: valid JSON extraction, three missing-field fallback shapes, and the malformed/empty-JSON catch-branch passthrough. This closes part of the coverage gap on `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` (currently 3.8% line coverage against a 60% threshold) and protects against a regression that would silently return empty meeting-task summaries.

## Context
The method under test, `PlaudCliClient.ParseSummaryJson`, lives at `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` lines 58-80:

```csharp
public static PlaudSummaryResult ParseSummaryJson(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var headline = root.TryGetProperty("header", out var header) &&
                       header.TryGetProperty("headline", out var h)
            ? h.GetString() ?? string.Empty
            : string.Empty;

        var content = root.TryGetProperty("ai_content", out var c)
            ? c.GetString() ?? string.Empty
            : string.Empty;

        return new PlaudSummaryResult(headline, content);
    }
    catch (JsonException)
    {
        return new PlaudSummaryResult(string.Empty, json);
    }
}
```

`PlaudSummaryResult` is `sealed record PlaudSummaryResult(string Headline, string MarkdownContent)` defined in `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudSummaryResult.cs`.

The target test file is `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs`. It is a `public sealed class PlaudCliClientParserTests` in namespace `Anela.Heblo.Adapters.Plaud.Tests`, with `using FluentAssertions;` at the top (line 1). All existing tests are discrete `[Fact]` methods (no `[Theory]`), named `<MethodUnderTest>_<Scenario>_<ExpectedOutcome>`, calling the target static method directly and asserting via FluentAssertions' `.Should()`. The file currently ends with `ParseFileDetail_IgnoresHeaderLines` at lines 95-112, immediately before the closing `}` of the class at line 113:

```csharp
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
}
```

No fixtures are needed — all new tests use inline JSON string literals, matching the `ParseFileDetail_IgnoresHeaderLines` pattern of an inline `const string input = """ ... """;` raw string literal. `PlaudCliClient` is directly accessible (same assembly's internals via `ProjectReference`, and the method/class are `public`), so no new `using` statements are required beyond what's already in the file.

## Files to create/modify
- `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs` — append six new `[Fact]` test methods after `ParseFileDetail_IgnoresHeaderLines` (before the class's closing brace), covering `ParseSummaryJson`.

## Implementation steps
1. Open `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientParserTests.cs` and locate the end of `ParseFileDetail_IgnoresHeaderLines` (the `}` that closes that method, just before the class's final closing `}`).
2. Insert the following six `[Fact]` methods after `ParseFileDetail_IgnoresHeaderLines`, in this order, each calling `PlaudCliClient.ParseSummaryJson` and asserting via FluentAssertions:

   a. `ParseSummaryJson_WithValidJson_ExtractsHeadlineAndContent` — well-formed JSON with both fields present:
      ```csharp
      [Fact]
      public void ParseSummaryJson_WithValidJson_ExtractsHeadlineAndContent()
      {
          const string json = """{"header":{"headline":"Weekly Sync"},"ai_content":"# Notes\n- item one"}""";

          var result = PlaudCliClient.ParseSummaryJson(json);

          result.Headline.Should().Be("Weekly Sync");
          result.MarkdownContent.Should().Be("# Notes\n- item one");
      }
      ```
      Note: since the JSON literal is itself a C# string, the `\n` inside the JSON string value must be a literal backslash-n *within the JSON text* (so the JSON parser sees an escaped newline and decodes it to an actual newline character in the parsed string) — write the input as a normal (non-raw, non-verbatim) C# string `"{\"header\":{\"headline\":\"Weekly Sync\"},\"ai_content\":\"# Notes\\n- item one\"}"` so `\\n` in C# source produces the two characters `\` and `n` in the string passed to `JsonDocument.Parse`, which JSON then decodes to a real newline in `ai_content`. Do not use a triple-quoted raw string literal here (raw strings don't process `\n` escapes), and assert the expected value with an actual embedded newline (`"# Notes\n- item one"` as a normal C# string, where `\n` here **is** processed by the C# compiler into a real newline character).

   b. `ParseSummaryJson_WithMissingHeader_ReturnsEmptyHeadline` — `header` object absent entirely:
      ```csharp
      [Fact]
      public void ParseSummaryJson_WithMissingHeader_ReturnsEmptyHeadline()
      {
          const string json = """{"ai_content":"body text"}""";

          var result = PlaudCliClient.ParseSummaryJson(json);

          result.Headline.Should().Be(string.Empty);
          result.MarkdownContent.Should().Be("body text");
      }
      ```

   c. `ParseSummaryJson_WithHeaderButNoHeadline_ReturnsEmptyHeadline` — `header` present but `headline` missing:
      ```csharp
      [Fact]
      public void ParseSummaryJson_WithHeaderButNoHeadline_ReturnsEmptyHeadline()
      {
          const string json = """{"header":{},"ai_content":"body text"}""";

          var result = PlaudCliClient.ParseSummaryJson(json);

          result.Headline.Should().Be(string.Empty);
          result.MarkdownContent.Should().Be("body text");
      }
      ```

   d. `ParseSummaryJson_WithMissingAiContent_ReturnsEmptyContent` — `ai_content` absent entirely:
      ```csharp
      [Fact]
      public void ParseSummaryJson_WithMissingAiContent_ReturnsEmptyContent()
      {
          const string json = """{"header":{"headline":"Title Only"}}""";

          var result = PlaudCliClient.ParseSummaryJson(json);

          result.Headline.Should().Be("Title Only");
          result.MarkdownContent.Should().Be(string.Empty);
      }
      ```

   e. `ParseSummaryJson_WithMalformedJson_ReturnsEmptyHeadlineAndRawContent` — invalid JSON triggers the `JsonException` catch branch and returns the raw input verbatim as `MarkdownContent`:
      ```csharp
      [Fact]
      public void ParseSummaryJson_WithMalformedJson_ReturnsEmptyHeadlineAndRawContent()
      {
          const string json = "{ this is not valid json";

          var result = PlaudCliClient.ParseSummaryJson(json);

          result.Headline.Should().Be(string.Empty);
          result.MarkdownContent.Should().Be(json);
      }
      ```
      Asserting `result.MarkdownContent.Should().Be(json)` against the same `json` local used as input proves passthrough of the exact original string (FluentAssertions' `Be` does value/string equality, which is sufficient to prove the source string was passed through unmodified — do not reconstruct or retype the literal in the assertion).

   f. `ParseSummaryJson_WithEmptyString_ReturnsEmptyHeadlineAndEmptyContent` — empty-string edge case, also hits the `JsonException` catch branch since `JsonDocument.Parse("")` throws:
      ```csharp
      [Fact]
      public void ParseSummaryJson_WithEmptyString_ReturnsEmptyHeadlineAndEmptyContent()
      {
          var result = PlaudCliClient.ParseSummaryJson(string.Empty);

          result.Headline.Should().Be(string.Empty);
          result.MarkdownContent.Should().Be(string.Empty);
      }
      ```

3. Save the file. Ensure the six new methods are inside the `PlaudCliClientParserTests` class body (before its closing `}`) and that no existing test method or the class declaration was altered.
4. From the repository root, build and run just this test project to confirm all new (and existing) tests pass:
   ```
   dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj
   ```
   Confirm the output reports 0 failed and includes the 6 new test names (e.g. via `--filter "FullyQualifiedName~ParseSummaryJson"` if you want to isolate them first), then run the full project's test suite once more without the filter to confirm nothing else regressed. Also run `dotnet format backend/test/Anela.Heblo.Adapters.Plaud.Tests/Anela.Heblo.Adapters.Plaud.Tests.csproj --verify-no-changes` (or `dotnet format` without `--verify-no-changes` to auto-fix, then re-check `git diff`) to confirm formatting matches repository conventions before finishing.

---
