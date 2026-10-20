# KB Question Tags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tag every Poradenství (KB) question with problem and product tags (plus the product codes the answer recommended), and let staff filter and count questions by period, problem and product.

**Architecture:** The existing LLM query-expansion call (`RagQueryExpander`, Haiku) is changed to return JSON with the expanded query plus tags. A parser keeps today's behaviour as the fallback. A KB-scoped tag library feeds already-used tags back into the prompt, so the vocabulary builds itself from an empty start. Recommended product codes are collected deterministically by `PostAnswerEnrichmentMiddleware`. Tags live in `text[]` columns on `RagInteractionLogs`; the feedback list gains date and tag filters, and a new `tag-stats` endpoint returns exact counts.

**Tech Stack:** .NET 8, MediatR, EF Core 8 + Npgsql (`text[]` primitive collections), Microsoft.Extensions.AI, xUnit + Moq + FluentAssertions, Testcontainers (pgvector/pg16); React + TanStack Query + Tailwind, Jest via `react-scripts test`.

**Spec:** `docs/superpowers/specs/2026-10-14-kb-question-tags-design.md`

## Global Constraints

- DTOs exposed via the API are **classes, never records**. Internal application/domain types may be records.
- Every Application `*Response` must inherit `BaseResponse` (a reflection contract test enforces it).
- Database migrations are applied **manually**; the plan only generates the migration file.
- Tag normalisation: trim, collapse inner whitespace, `ToLowerInvariant`, drop empty values, de-duplicate, max **5** per kind (`MAX_TAGS_PER_KIND = 5`).
- Tag library prompt cap: top **200** tags per kind by count (`MAX_LIBRARY_TAGS_PER_KIND = 200`).
- Prompt placeholder string: `{EXISTING_TAGS}`; empty library text: `(zatím žádné)`.
- `AdditionalProperties` key for recommended codes: `anela.recommendedProductCodes`.
- Date filters are **inclusive** calendar days in **Europe/Prague**, converted to a half-open UTC range `[from, to + 1 day)`.
- Tag library and tag stats are scoped to `RagFeature.KnowledgeBase` only.
- New endpoints use `[FeatureAuthorize(Feature.Customer_KnowledgeBase, AccessLevel.Write)]`, the same as `feedback/list`.
- The frontend must not send `DateOnly` params through the generated client: it serializes them with `toISOString()` (full timestamp; also a day early for local midnight in CEST). Use a hand-written fetch with `${apiClient.baseUrl}${relativeUrl}`, then `GetFeedbackListResponse.fromJS(...)` so dates are `Date` objects again.
- Catch blocks that should not swallow caller cancellation filter on `!ct.IsCancellationRequested`, never on `ex is not OperationCanceledException` (an HTTP timeout is a `TaskCanceledException`).
- Backend test runs in this repo: build first, then `dotnet test --no-build -p:UseSharedCompilation=false` (concurrent worktrees hang otherwise). Integration tests need `podman machine start` and are excluded from CI (`Category=Integration`).
- Frontend tests: `cd frontend && CI=true npx react-scripts test --watchAll=false <pattern>`. Gate on `CI=false npm run build` (stricter than `tsc`).

## Review Focus

1. **Haiku wraps JSON in a ```json fence, or adds a sentence before it.** Expected: a fence is stripped and parsed; anything else not starting with `{` falls back to the raw text, untagged, with no crash. Pinned in Task 1 (`Parse_FencedJson…`, `Parse_ProseBeforeJson…`).
2. **Model returns valid JSON with `expanded` empty, or tags that aren't strings.** Expected: treated as malformed. The raw response is embedded (today's behaviour), a warning is logged, and the Ask still succeeds. Pinned in Task 1 (`Parse_BlankExpanded…`, `Parse_NonStringTags…`) and Task 2 (`ExpandAsync_MalformedJson_LogsWarning…`).
3. **Staff pick `DateFrom` later than `DateTo`, or the same day for both.** Expected: the same day returns that whole Prague day (00:00 to 24:00 local); reversed dates return an empty list, not a 400/500. Pinned in Task 7 (`Handle_SameDayRange…`, `Handle_ReversedRange…`).
4. **A tag filter typed or clicked with different case or spacing ("Akné ", "AKNÉ").** Expected: it matches the stored tag `akné`. Pinned in Task 7 (`Handle_NormalisesTagFilters…`).
5. **The tag library query fails (DB blip) during an Ask.** Expected: the Ask still answers. The prompt says `(zatím žádné)` and an error is logged; a caller-cancelled request still cancels. Pinned in Task 6 (`BuildExpansionConfigAsync_RepositoryThrows…`, `…CallerCancelled_Rethrows`).

---

## File Map

**Create**
- `backend/src/Anela.Heblo.Application/Shared/Rag/RagExpansionResult.cs`: result record (embed text plus tags).
- `backend/src/Anela.Heblo.Application/Shared/Rag/RagExpansionParser.cs`: pure parser and tag normaliser.
- `backend/src/Anela.Heblo.Domain/Features/Rag/RagFeedbackLogFilter.cs`: list filter record.
- `backend/src/Anela.Heblo.Domain/Features/Rag/RagTagCount.cs`: `RagTagKind` + `RagTagCount`.
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IQuestionTagLibrary.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/QuestionTagLibrary.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PragueDateRange.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetQuestionTagStats/GetQuestionTagStatsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetQuestionTagStats/GetQuestionTagStatsHandler.cs`
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddRagInteractionLogTags.cs` (generated)
- Tests: `backend/test/Anela.Heblo.Tests/Shared/Rag/RagExpansionParserTests.cs`, `…/KnowledgeBase/Services/QuestionTagLibraryTests.cs`, `…/KnowledgeBase/Services/PragueDateRangeTests.cs`, `…/KnowledgeBase/UseCases/GetQuestionTagStatsHandlerTests.cs`, `…/KnowledgeBase/Integration/RagInteractionLogTagsIntegrationTests.cs`
- `frontend/src/components/feedback/KbQuestionTagFilters.tsx`, `frontend/src/components/feedback/KbTagFrequencyPanel.tsx`, `frontend/src/components/feedback/TagChips.tsx` + tests

**Modify**
- `Shared/Rag/IRagQueryExpander.cs`, `Shared/Rag/RagQueryExpander.cs`, `Shared/Rag/RagInteractionRecorder.cs`, `Shared/Rag/RagInteractionLogFactory.cs`, `Shared/Rag/RagFeedbackLogSummary.cs`, `Shared/Rag/RagFeedbackListMapper.cs`
- `Domain/Features/Rag/RagInteractionLog.cs`, `Domain/Features/Rag/IRagInteractionLogRepository.cs`
- `Persistence/Rag/RagInteractionLogConfiguration.cs`, `Persistence/Rag/RagInteractionLogRepository.cs`
- `Features/KnowledgeBase/KnowledgeBaseOptions.cs` (prompt), `KnowledgeBaseModule.cs` (DI)
- `Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs`, `…/AskQuestion/AskQuestionHandler.cs`, `…/GetFeedbackList/*`
- `Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs`
- `Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`
- `Features/Smartsupp/UseCases/GetDraftReplyFeedbackList/GetDraftReplyFeedbackListHandler.cs`
- `API/Controllers/KnowledgeBaseController.cs`
- Tests touched by signature changes: `RagQueryExpanderTests`, `SearchDocumentsHandlerTests`, `GenerateLeafletHandlerTests`, `GetFeedbackListHandlerTests`, `GetDraftReplyFeedbackListHandlerTests`, `KnowledgeBaseRepositoryIntegrationTests` (schema), `AskQuestionHandlerTests`, `PostAnswerEnrichmentMiddlewareTests`
- Frontend: `api/hooks/useKnowledgeBase.ts`, `components/feedback/ragFeedbackTypes.ts`, `components/feedback/ragFeedbackMapping.ts`, `components/feedback/types.ts`, `components/feedback/adapters/useKbFeedbackAdapter.ts`, `components/feedback/GenericFeedbackTable.tsx`, `components/feedback/RagFeedbackDetailExtra.tsx`, `pages/MarketingFeedbackPage.tsx`

Paths below are relative to the repo root. `App` = `backend/src/Anela.Heblo.Application`, `Tests` = `backend/test/Anela.Heblo.Tests`.

---

### Task 1: Expansion parser

**Files:**
- Create: `App/Shared/Rag/RagExpansionResult.cs`
- Create: `App/Shared/Rag/RagExpansionParser.cs`
- Test: `Tests/Shared/Rag/RagExpansionParserTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record RagExpansionResult(string QueryToEmbed, IReadOnlyList<string> ProblemTags, IReadOnlyList<string> ProductTags)` with `static RagExpansionResult Untagged(string queryToEmbed)`
  - `public sealed record RagExpansionParseResult(RagExpansionResult Expansion, bool IsMalformedJson)`
  - `public static class RagExpansionParser` with `const int MaxTagsPerKind = 5`, `RagExpansionParseResult Parse(string rawResponse)`, `IReadOnlyList<string> NormalizeTags(IEnumerable<string?>? tags)`, `string? NormalizeTag(string? tag)`

- [ ] **Step 1: Write the failing tests**

`Tests/Shared/Rag/RagExpansionParserTests.cs`:

```csharp
using Anela.Heblo.Application.Shared.Rag;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Shared.Rag;

public class RagExpansionParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsExpandedAndNormalisedTags()
    {
        const string raw = """{"expanded": "Problém: akné na čele", "problemTags": ["Akné"], "productTags": ["Hořčíkové máslo"]}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: akné na čele");
        result.Expansion.ProblemTags.Should().Equal("akné");
        result.Expansion.ProductTags.Should().Equal("hořčíkové máslo");
    }

    [Fact]
    public void Parse_FencedJson_StripsFenceAndParses()
    {
        const string raw = "```json\n{\"expanded\": \"Problém: růžovka\", \"problemTags\": [\"růžovka\"], \"productTags\": []}\n```";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: růžovka");
        result.Expansion.ProblemTags.Should().Equal("růžovka");
        result.Expansion.ProductTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlainText_ReturnsRawUntaggedWithoutMalformedFlag()
    {
        const string raw = "Problém: popraskané ruce\nKontext: suchá kůže";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
        result.Expansion.ProductTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ProseBeforeJson_FallsBackToRawUntagged()
    {
        const string raw = "Tady je výstup: {\"expanded\": \"x\", \"problemTags\": [], \"productTags\": []}";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TruncatedJson_FlagsMalformedAndReturnsRaw()
    {
        const string raw = "{\"expanded\": \"Problém: akné\", \"problemTags\": [\"akn";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeTrue();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BlankExpanded_FlagsMalformedAndReturnsRaw()
    {
        const string raw = """{"expanded": "  ", "problemTags": ["akné"], "productTags": []}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeTrue();
        result.Expansion.QueryToEmbed.Should().Be(raw);
        result.Expansion.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NonStringTags_FlagsMalformedAndReturnsRaw()
    {
        const string raw = """{"expanded": "Problém: akné", "problemTags": [{"name": "akné"}], "productTags": []}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeTrue();
        result.Expansion.QueryToEmbed.Should().Be(raw);
    }

    [Fact]
    public void Parse_MissingTagArrays_ReturnsEmptyTags()
    {
        const string raw = """{"expanded": "Problém: akné"}""";

        var result = RagExpansionParser.Parse(raw);

        result.IsMalformedJson.Should().BeFalse();
        result.Expansion.QueryToEmbed.Should().Be("Problém: akné");
        result.Expansion.ProblemTags.Should().BeEmpty();
        result.Expansion.ProductTags.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeTags_TrimsLowercasesCollapsesWhitespaceDedupesAndCaps()
    {
        var tags = new string?[] { " Akné ", "akné", "suchá   pleť", "", null, "a", "b", "c", "d", "e" };

        var result = RagExpansionParser.NormalizeTags(tags);

        result.Should().Equal("akné", "suchá pleť", "a", "b", "c");
    }

    [Fact]
    public void NormalizeTags_Null_ReturnsEmpty()
    {
        RagExpansionParser.NormalizeTags(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(" AKNÉ ", "akné")]
    [InlineData("otoky   nohou", "otoky nohou")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeTag_NormalisesSingleValue(string? input, string? expected)
    {
        RagExpansionParser.NormalizeTag(input).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -5
```
Expected: build FAILS with `The name 'RagExpansionParser' does not exist in the current context`.

- [ ] **Step 3: Implement**

`App/Shared/Rag/RagExpansionResult.cs`:

```csharp
namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// Output of query expansion: the text to embed for vector search, plus the problem/product tags the
/// model extracted from the question. Tags are never part of <see cref="QueryToEmbed"/> — appending them
/// would pollute the query vector.
/// </summary>
public sealed record RagExpansionResult(
    string QueryToEmbed,
    IReadOnlyList<string> ProblemTags,
    IReadOnlyList<string> ProductTags)
{
    public static RagExpansionResult Untagged(string queryToEmbed) => new(queryToEmbed, [], []);
}

/// <summary>Parse outcome; <see cref="IsMalformedJson"/> is true only when the text looked like JSON but could not be used.</summary>
public sealed record RagExpansionParseResult(RagExpansionResult Expansion, bool IsMalformedJson);
```

`App/Shared/Rag/RagExpansionParser.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Anela.Heblo.Application.Shared.Rag;

/// <summary>
/// Parses the query-expansion LLM response. JSON (optionally wrapped in a Markdown code fence) yields the
/// expanded query plus normalised tags; anything else falls back to the whole raw response as the text to
/// embed with no tags — exactly the pre-tagging behaviour, so a bad prompt can only lose tags, never answers.
/// </summary>
public static class RagExpansionParser
{
    public const int MaxTagsPerKind = 5;

    private static readonly Regex CodeFence = new(
        @"^```(?:json)?\s*(?<body>.*?)\s*```$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex InnerWhitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static RagExpansionParseResult Parse(string rawResponse)
    {
        var trimmed = rawResponse.Trim();
        var fence = CodeFence.Match(trimmed);
        var candidate = fence.Success ? fence.Groups["body"].Value.Trim() : trimmed;

        if (!candidate.StartsWith('{'))
            return new RagExpansionParseResult(RagExpansionResult.Untagged(rawResponse), IsMalformedJson: false);

        ExpansionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ExpansionPayload>(candidate, JsonOptions);
        }
        catch (JsonException)
        {
            return new RagExpansionParseResult(RagExpansionResult.Untagged(rawResponse), IsMalformedJson: true);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Expanded))
            return new RagExpansionParseResult(RagExpansionResult.Untagged(rawResponse), IsMalformedJson: true);

        var expansion = new RagExpansionResult(
            payload.Expanded.Trim(),
            NormalizeTags(payload.ProblemTags),
            NormalizeTags(payload.ProductTags));

        return new RagExpansionParseResult(expansion, IsMalformedJson: false);
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string?>? tags) =>
        (tags ?? [])
            .Select(NormalizeTag)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxTagsPerKind)
            .ToList();

    public static string? NormalizeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        return InnerWhitespace.Replace(tag.Trim(), " ").ToLowerInvariant();
    }

    private sealed class ExpansionPayload
    {
        [JsonPropertyName("expanded")]
        public string? Expanded { get; init; }

        [JsonPropertyName("problemTags")]
        public List<string?>? ProblemTags { get; init; }

        [JsonPropertyName("productTags")]
        public List<string?>? ProductTags { get; init; }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RagExpansionParserTests"
```
Expected: 14 passed (11 facts + the 4-case theory counted per case).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/Rag/RagExpansionResult.cs backend/src/Anela.Heblo.Application/Shared/Rag/RagExpansionParser.cs backend/test/Anela.Heblo.Tests/Shared/Rag/RagExpansionParserTests.cs
git commit -m "feat: add RAG expansion parser for tagged query expansion"
```

---

### Task 2: Expander returns `RagExpansionResult`

Retrieval behaviour stays identical: both call sites embed `QueryToEmbed`, and every existing expectation about the embedded text stays as it is.

**Files:**
- Modify: `App/Shared/Rag/IRagQueryExpander.cs`, `App/Shared/Rag/RagQueryExpander.cs`
- Modify: `App/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs:40`
- Modify: `App/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs:47-48`
- Test: `Tests/Shared/Rag/RagQueryExpanderTests.cs`, `Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs`, `Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`

**Interfaces:**
- Consumes: `RagExpansionParser.Parse`, `RagExpansionResult.Untagged` (Task 1)
- Produces: `Task<RagExpansionResult> IRagQueryExpander.ExpandAsync(string query, RagQueryExpansionConfig config, CancellationToken ct)`

- [ ] **Step 1: Write the new failing expander tests** (append to `RagQueryExpanderTests`)

```csharp
    [Fact]
    public async Task ExpandAsync_JsonResponse_ReturnsExpandedQueryAndTags()
    {
        // Arrange
        SetupChatClient("""{"expanded": "Problém: akné", "problemTags": ["akné"], "productTags": ["hořčíkové máslo"]}""");
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("mám akné, pomůže máslo?", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("Problém: akné");
        result.ProblemTags.Should().Equal("akné");
        result.ProductTags.Should().Equal("hořčíkové máslo");
    }

    [Fact]
    public async Task ExpandAsync_MalformedJson_LogsWarningAndEmbedsRawResponse()
    {
        // Arrange
        const string malformed = "{\"expanded\": \"Problém: ak";
        SetupChatClient(malformed);
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("dotaz", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be(malformed);
        result.ProblemTags.Should().BeEmpty();
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExpandAsync_PlainTextResponse_DoesNotLogWarning()
    {
        // Arrange
        SetupChatClient("Problém: popraskané ruce");
        var expander = CreateExpander();

        // Act
        var result = await expander.ExpandAsync("dotaz", EnabledConfig(), CancellationToken.None);

        // Assert
        result.QueryToEmbed.Should().Be("Problém: popraskané ruce");
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
```

- [ ] **Step 2: Update the existing expander assertions to the new return type**

In `RagQueryExpanderTests.cs`, change each existing string assertion on `result` to `result.QueryToEmbed`, and add an empty-tags assertion to the disabled/failure cases:

| Line | Before | After |
|---|---|---|
| 43 | `result.Should().Be("my raw query");` | `result.QueryToEmbed.Should().Be("my raw query");` then `result.ProblemTags.Should().BeEmpty();` |
| 63 | `result.Should().Be("   ");` | `result.QueryToEmbed.Should().Be("   ");` |
| 83 | `result.Should().Be("EXPANDED");` | `result.QueryToEmbed.Should().Be("EXPANDED");` |
| 123 | `result.Should().Be("original query");` | `result.QueryToEmbed.Should().Be("original query");` then `result.ProblemTags.Should().BeEmpty();` |
| 147 | `result.Should().Be("original query");` | `result.QueryToEmbed.Should().Be("original query");` |

- [ ] **Step 3: Update the mock setups of the two call sites**

In `Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs`:
- Replace every `.ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);` (lines 61, 87, 110, 203, 233, 268, 294, 333) with `.ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => RagExpansionResult.Untagged(q));`
- Line 133: `.ReturnsAsync(expandedQuery);` → `.ReturnsAsync(RagExpansionResult.Untagged(expandedQuery));`
- Line 167: `.ReturnsAsync(rawQuery);` → `.ReturnsAsync(RagExpansionResult.Untagged(rawQuery));`

In `Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`:
- Line 34: `=> q);` → `=> RagExpansionResult.Untagged(q));`
- Line 465: `.ReturnsAsync(expandedQuery);` → `.ReturnsAsync(RagExpansionResult.Untagged(expandedQuery));`

Check with: `grep -n "ExpandAsync" -A6 <file> | grep ReturnsAsync`. Every match must contain `RagExpansionResult`.

- [ ] **Step 4: Run to verify failure**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep -E "error" | head -5
```
Expected: build errors such as `'string' does not contain a definition for 'QueryToEmbed'` (the interface still returns `string`).

- [ ] **Step 5: Implement**

`App/Shared/Rag/IRagQueryExpander.cs`: change only the method signature:

```csharp
    Task<RagExpansionResult> ExpandAsync(string query, RagQueryExpansionConfig config, CancellationToken ct);
```

`App/Shared/Rag/RagQueryExpander.cs`: replace the body of `ExpandAsync`:

```csharp
    public async Task<RagExpansionResult> ExpandAsync(string query, RagQueryExpansionConfig config, CancellationToken ct)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(config.Prompt))
        {
            return RagExpansionResult.Untagged(query);
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, config.Prompt + "\n" + query)
        };

        var chatOptions = new ChatOptions { ModelId = config.Model };
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
            if (string.IsNullOrWhiteSpace(response.Text))
                return RagExpansionResult.Untagged(query);

            var parsed = RagExpansionParser.Parse(response.Text);
            if (parsed.IsMalformedJson)
            {
                _logger.LogWarning(
                    "Query expansion returned malformed JSON for query '{Query}'; embedding raw response without tags",
                    query);
            }

            return parsed.Expansion;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Query expansion failed for query '{Query}', falling back to raw query", query);
            return RagExpansionResult.Untagged(query);
        }
    }
```

`SearchDocumentsHandler.cs` line 40: replace

```csharp
        var queryToEmbed = await _expander.ExpandAsync(request.Query, _options.ToExpansionConfig(), cancellationToken);
```
with
```csharp
        var expansion = await _expander.ExpandAsync(request.Query, _options.ToExpansionConfig(), cancellationToken);
        var queryToEmbed = expansion.QueryToEmbed;
```

`GenerateLeafletHandler.cs` lines 47-48: replace

```csharp
        var queryToEmbed = await _expander.ExpandAsync(
            request.Topic, _options.ToExpansionConfig(), ct);
```
with
```csharp
        // Leaflet's prompt is plain text, so tags are always empty here; only the embed text is used.
        var queryToEmbed = (await _expander.ExpandAsync(
            request.Topic, _options.ToExpansionConfig(), ct)).QueryToEmbed;
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RagQueryExpanderTests|FullyQualifiedName~SearchDocumentsHandlerTests|FullyQualifiedName~GenerateLeafletHandlerTests"
```
Expected: all pass, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/Rag backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs backend/test/Anela.Heblo.Tests/Shared/Rag/RagQueryExpanderTests.cs backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs
git commit -m "refactor: return structured RagExpansionResult from query expander"
```

---

### Task 3: Tag columns, EF mapping and migration

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Rag/RagInteractionLog.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Rag/RagInteractionLogConfiguration.cs`
- Create (generated): `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddRagInteractionLogTags.cs` + `.Designer.cs`, updated `ApplicationDbContextModelSnapshot.cs`
- Modify: `Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs:104-125` (hand-written schema)
- Test: `Tests/KnowledgeBase/Integration/RagInteractionLogTagsIntegrationTests.cs`

**Interfaces:**
- Produces: `RagInteractionLog.ProblemTags`, `.ProductTags`, `.RecommendedProductCodes`, each `List<string>` initialised to `[]`.

- [ ] **Step 1: Write the failing integration test**

`Tests/KnowledgeBase/Integration/RagInteractionLogTagsIntegrationTests.cs`. It reuses the same container setup as `KnowledgeBaseRepositoryIntegrationTests` but creates the table **through the EF model** (`EnsureCreatedAsync`), so the mapping itself is what gets tested. Reads go through a **second** `DbContext`, so the test can't pass by reading back the tracked instance.

```csharp
using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Rag;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Integration;

[Trait("Category", "Integration")]
public class RagInteractionLogTagsIntegrationTests : IAsyncLifetime
{
    static RagInteractionLogTagsIntegrationTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var builder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        builder.UseVector();
        _dataSource = builder.Build();

        await using var setup = CreateContext();
        await setup.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_dataSource)
            .AllowManyServiceProviders()
            .Options);

    internal static RagInteractionLog MakeLog(
        DateTimeOffset createdAt,
        RagFeature feature = RagFeature.KnowledgeBase,
        List<string>? problemTags = null,
        List<string>? productTags = null,
        List<string>? recommended = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Feature = feature,
            CreatedAt = createdAt,
            Question = "q",
            Answer = "a",
            TopK = 5,
            SourceCount = 0,
            RetrievedChunksJson = "[]",
            SystemPrompt = "",
            DurationMs = 1,
            ProblemTags = problemTags ?? [],
            ProductTags = productTags ?? [],
            RecommendedProductCodes = recommended ?? [],
        };

    [Fact]
    public async Task SaveAsync_PersistsTagArrays()
    {
        // Arrange
        var log = MakeLog(DateTimeOffset.UtcNow, problemTags: ["akné", "suchá pleť"], productTags: ["hořčíkové máslo"], recommended: ["MAS009"]);
        await using (var writeContext = CreateContext())
        {
            await new RagInteractionLogRepository(writeContext).SaveAsync(log);
        }

        // Act
        await using var readContext = CreateContext();
        var loaded = await readContext.RagInteractionLogs.SingleAsync(l => l.Id == log.Id);

        // Assert
        loaded.ProblemTags.Should().Equal("akné", "suchá pleť");
        loaded.ProductTags.Should().Equal("hořčíkové máslo");
        loaded.RecommendedProductCodes.Should().Equal("MAS009");
    }

    [Fact]
    public async Task SaveAsync_EmptyTags_PersistsEmptyArraysNotNull()
    {
        // Arrange
        var log = MakeLog(DateTimeOffset.UtcNow);
        await using (var writeContext = CreateContext())
        {
            await new RagInteractionLogRepository(writeContext).SaveAsync(log);
        }

        // Act
        await using var readContext = CreateContext();
        var loaded = await readContext.RagInteractionLogs.SingleAsync(l => l.Id == log.Id);

        // Assert
        loaded.ProblemTags.Should().NotBeNull().And.BeEmpty();
        loaded.ProductTags.Should().NotBeNull().And.BeEmpty();
        loaded.RecommendedProductCodes.Should().NotBeNull().And.BeEmpty();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
podman machine start 2>/dev/null; dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep error | head -3
```
Expected: `'RagInteractionLog' does not contain a definition for 'ProblemTags'`.

- [ ] **Step 3: Add the entity properties**

In `RagInteractionLog.cs`, add after `DurationMs`:

```csharp
    // --- Question tags (KB analytics; see docs/superpowers/specs/2026-10-14-kb-question-tags-design.md) ---

    /// <summary>Skin/body problems or care goals the question is about, normalised (lowercase, nominative singular).</summary>
    public List<string> ProblemTags { get; set; } = [];

    /// <summary>Products the customer named in the question (free-text names, normalised).</summary>
    public List<string> ProductTags { get; set; } = [];

    /// <summary>Catalog codes of products the generated answer linked to (resolved by PostAnswerEnrichmentMiddleware).</summary>
    public List<string> RecommendedProductCodes { get; set; } = [];
```

- [ ] **Step 4: Map the columns**

In `RagInteractionLogConfiguration.cs`, add after the `DurationMs` property line:

```csharp
        builder.Property(x => x.ProblemTags).HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(x => x.ProductTags).HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'::text[]");
        builder.Property(x => x.RecommendedProductCodes).HasColumnType("text[]").IsRequired().HasDefaultValueSql("'{}'::text[]");
```

and after the existing `HasIndex` lines:

```csharp
        builder.HasIndex(x => x.ProblemTags).HasMethod("gin");
        builder.HasIndex(x => x.ProductTags).HasMethod("gin");
```

- [ ] **Step 5: Update the hand-written schema in the existing integration test**

In `KnowledgeBaseRepositoryIntegrationTests.cs`, change the end of the `RagInteractionLogs` CREATE TABLE from

```sql
                "DurationMs"          bigint NOT NULL
            );
```
to
```sql
                "DurationMs"          bigint NOT NULL,
                "ProblemTags"             text[] NOT NULL DEFAULT '{}',
                "ProductTags"             text[] NOT NULL DEFAULT '{}',
                "RecommendedProductCodes" text[] NOT NULL DEFAULT '{}'
            );
```

- [ ] **Step 6: Generate the migration**

```bash
dotnet ef migrations add AddRagInteractionLogTags --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```
Then check the generated `Up` contains exactly three `AddColumn<List<string>>` calls on table `RagInteractionLogs` in schema `public` (`type: "text[]"`, `nullable: false`, `defaultValueSql: "'{}'::text[]"`) and two `CreateIndex` calls with `.Annotation("Npgsql:IndexMethod", "gin")`. It must contain **no** other table changes. If it does, the snapshot was stale: stop and report, don't hand-edit unrelated operations.

```bash
grep -cE "AddColumn|CreateIndex" backend/src/Anela.Heblo.Persistence/Migrations/*_AddRagInteractionLogTags.cs
```
Expected: `5`.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~RagInteractionLogTagsIntegrationTests|FullyQualifiedName~KnowledgeBaseRepositoryIntegrationTests"
```
Expected: all pass. If `EnsureCreatedAsync` fails on an unrelated entity (e.g. a pgvector or extension dependency), add the missing `CREATE EXTENSION` to `InitializeAsync` rather than switching to hand-written SQL. The point of this test is to verify the EF mapping.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Rag/RagInteractionLog.cs backend/src/Anela.Heblo.Persistence/Rag/RagInteractionLogConfiguration.cs backend/src/Anela.Heblo.Persistence/Migrations backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration
git commit -m "feat: add question tag columns to RagInteractionLogs"
```

---

### Task 4: Record question tags on every search

**Files:**
- Modify: `App/Shared/Rag/RagInteractionRecorder.cs`
- Modify: `App/Shared/Rag/RagInteractionLogFactory.cs`
- Modify: `App/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs`
- Test: `Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs`, `Tests/KnowledgeBase/Pipeline/QuestionLoggingBehaviorTests.cs`

**Interfaces:**
- Consumes: `RagExpansionResult` (Task 1), `RagInteractionLog` tag properties (Task 3)
- Produces on `IRagInteractionRecorder`:
  - `IReadOnlyList<string> ProblemTags { get; }`, `IReadOnlyList<string> ProductTags { get; }`, `IReadOnlyList<string> RecommendedProductCodes { get; }`
  - `void RecordQuestionTags(IReadOnlyList<string> problemTags, IReadOnlyList<string> productTags)`
  - `void RecordRecommendedProducts(IReadOnlyList<string> productCodes)`

(Refinement of spec §4: the spec put the tags on `RecordRetrieval`. Separate methods avoid changing the 4 existing `RecordRetrieval(question, topK, chunks)` test call sites, and mirror `RecordRecommendedProducts`.)

- [ ] **Step 1: Write the failing tests**

Append to `SearchDocumentsHandlerTests`:

```csharp
    [Fact]
    public async Task Handle_RecordsQuestionTagsFromExpansion()
    {
        // Arrange
        SetupEmbeddingGenerator();
        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagExpansionResult("Problém: akné", ["akné"], ["hořčíkové máslo"]));
        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default))
            .ReturnsAsync([]);

        // Act
        await CreateHandler().Handle(new SearchDocumentsRequest { Query = "mám akné", TopK = 5 }, default);

        // Assert
        Assert.Equal(["akné"], _recorder.ProblemTags);
        Assert.Equal(["hořčíkové máslo"], _recorder.ProductTags);
        Assert.Equal("Problém: akné", _recorder.ExpandedQuery);
    }

    [Fact]
    public async Task Handle_EmbeddingFails_StillRecordsQuestionTags()
    {
        // Arrange
        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagExpansionResult("Problém: akné", ["akné"], []));
        _embeddingGenerator
            .Setup(s => s.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), default))
            .ThrowsAsync(new HttpRequestException("down"));

        // Act
        await CreateHandler().Handle(new SearchDocumentsRequest { Query = "mám akné", TopK = 5 }, default);

        // Assert
        Assert.Equal(["akné"], _recorder.ProblemTags);
    }
```

Append to `QuestionLoggingBehaviorTests`. The file already has a real `_recorder`, `_repository`, `_userService` and `CreateBehavior()`. The user must be set up: the behavior calls `GetCurrentUser()` inside its try block, and an unset mock would throw there, be swallowed, and fail this test for the wrong reason.

```csharp
    [Fact]
    public async Task Handle_PersistsQuestionTagsAndRecommendedCodes()
    {
        // Arrange
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        _recorder.RecordRetrieval("Problém: akné", 5, []);
        _recorder.RecordQuestionTags(["akné"], ["hořčíkové máslo"]);
        _recorder.RecordInteraction(RagFeature.KnowledgeBase, "mám akné", "system prompt", "odpověď");
        _recorder.RecordRecommendedProducts(["MAS009"]);

        RagInteractionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .Callback<RagInteractionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        // Act
        await CreateBehavior().Handle(
            new AskQuestionRequest { Question = "mám akné", TopK = 5 },
            () => Task.FromResult(new AskQuestionResponse { Answer = "odpověď", Sources = [] }),
            default);

        // Assert
        Assert.NotNull(capturedLog);
        Assert.Equal(["akné"], capturedLog!.ProblemTags);
        Assert.Equal(["hořčíkové máslo"], capturedLog.ProductTags);
        Assert.Equal(["MAS009"], capturedLog.RecommendedProductCodes);
    }

    [Fact]
    public async Task Handle_NoTagsRecorded_PersistsEmptyLists()
    {
        // Arrange
        _userService.Setup(s => s.GetCurrentUser()).Returns(new CurrentUser("user-1", "Test User", null, true));
        RecordInteraction();

        RagInteractionLog? capturedLog = null;
        _repository
            .Setup(r => r.SaveAsync(It.IsAny<RagInteractionLog>(), It.IsAny<CancellationToken>()))
            .Callback<RagInteractionLog, CancellationToken>((log, _) => capturedLog = log)
            .Returns(Task.CompletedTask);

        // Act
        await CreateBehavior().Handle(
            new AskQuestionRequest { Question = "Test?", TopK = 5 },
            () => Task.FromResult(new AskQuestionResponse { Answer = "Test answer.", Sources = [] }),
            default);

        // Assert
        Assert.NotNull(capturedLog);
        Assert.Empty(capturedLog!.ProblemTags);
        Assert.Empty(capturedLog.ProductTags);
        Assert.Empty(capturedLog.RecommendedProductCodes);
    }
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep error | head -3
```
Expected: `'RagInteractionRecorder' does not contain a definition for 'ProblemTags'`.

- [ ] **Step 3: Implement**

In `RagInteractionRecorder.cs`, add to the interface after `string? Topic { get; }`:

```csharp
    IReadOnlyList<string> ProblemTags { get; }
    IReadOnlyList<string> ProductTags { get; }
    IReadOnlyList<string> RecommendedProductCodes { get; }
```

and after `RecordRetrieval`:

```csharp
    /// <summary>Tags extracted from the question by query expansion (empty when expansion produced none).</summary>
    void RecordQuestionTags(IReadOnlyList<string> problemTags, IReadOnlyList<string> productTags);

    /// <summary>Catalog codes the generated answer linked to.</summary>
    void RecordRecommendedProducts(IReadOnlyList<string> productCodes);
```

In the class, add the properties after `Topic`:

```csharp
    public IReadOnlyList<string> ProblemTags { get; private set; } = [];
    public IReadOnlyList<string> ProductTags { get; private set; } = [];
    public IReadOnlyList<string> RecommendedProductCodes { get; private set; } = [];
```

and the methods after `RecordRetrieval`:

```csharp
    public void RecordQuestionTags(IReadOnlyList<string> problemTags, IReadOnlyList<string> productTags)
    {
        ProblemTags = problemTags;
        ProductTags = productTags;
    }

    public void RecordRecommendedProducts(IReadOnlyList<string> productCodes)
    {
        RecommendedProductCodes = productCodes;
    }
```

In `RagInteractionLogFactory.Build`, add after `DurationMs = durationMs,`:

```csharp
            ProblemTags = recorder.ProblemTags.ToList(),
            ProductTags = recorder.ProductTags.ToList(),
            RecommendedProductCodes = recorder.RecommendedProductCodes.ToList(),
```

In `SearchDocumentsHandler.Handle`, directly after `var queryToEmbed = expansion.QueryToEmbed;` (Task 2):

```csharp
        // Recorded before embedding so the tags survive a transient embedding/vector-DB failure.
        _recorder.RecordQuestionTags(expansion.ProblemTags, expansion.ProductTags);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~SearchDocumentsHandlerTests|FullyQualifiedName~QuestionLoggingBehaviorTests|FullyQualifiedName~DraftReplyLoggingBehaviorTests"
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/Rag backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs backend/test/Anela.Heblo.Tests/KnowledgeBase
git commit -m "feat: record question tags on RAG interaction logs"
```

---

### Task 5: Recommended product codes from the answer

**Files:**
- Modify: `App/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs`
- Modify: `App/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`
- Test: `Tests/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddlewareTests.cs`, `Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs`

**Interfaces:**
- Consumes: `IRagInteractionRecorder.RecordRecommendedProducts` (Task 4)
- Produces: `public const string PostAnswerEnrichmentMiddleware.RecommendedProductCodesKey = "anela.recommendedProductCodes"`. The value stored under it is a `string[]` of known codes, de-duplicated, in order of first appearance.

- [ ] **Step 1: Write the failing middleware tests** (append to `PostAnswerEnrichmentMiddlewareTests`)

```csharp
    private static IReadOnlyList<string>? RecommendedCodes(ChatResponse response) =>
        response.AdditionalProperties?.TryGetValue(PostAnswerEnrichmentMiddleware.RecommendedProductCodesKey, out var value) == true
            ? value as IReadOnlyList<string>
            : null;

    [Fact]
    public async Task GetResponseAsync_KnownCodes_ExposedInAdditionalPropertiesDedupedInOrder()
    {
        SetupInner("Zkuste [Klidné nožky](MAS009), pak (AKL001) a znovu [nožky](MAS009).");
        SetupCache(("MAS009", "Klidné nožky", "https://anela.cz/klidne-nozky"), ("AKL001", "Sérum ABC", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(["MAS009", "AKL001"], RecommendedCodes(result));
    }

    [Fact]
    public async Task GetResponseAsync_UnknownCode_NotExposed()
    {
        SetupInner("Zkuste [Neexistující](FAKE01) a [Klidné nožky](MAS009).");
        SetupCache(("MAS009", "Klidné nožky", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(["MAS009"], RecommendedCodes(result));
    }

    [Fact]
    public async Task GetResponseAsync_NoProducts_ReturnsOriginalResponseWithoutKey()
    {
        SetupInner("Pijte dostatek vody.");
        SetupCache();

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Null(RecommendedCodes(result));
    }

    [Fact]
    public async Task GetResponseAsync_DoesNotMutateInnerResponseAdditionalProperties()
    {
        var innerProps = new AdditionalPropertiesDictionary { ["existing"] = "x" };
        _inner
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Zkuste (AKL001).")]) { AdditionalProperties = innerProps });
        SetupCache(("AKL001", "Sérum ABC", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.False(innerProps.ContainsKey(PostAnswerEnrichmentMiddleware.RecommendedProductCodesKey));
        Assert.Equal("x", result.AdditionalProperties!["existing"]);
    }
```

- [ ] **Step 2: Write the failing handler tests** (append to `AskQuestionHandlerTests`)

```csharp
    [Fact]
    public async Task Handle_RecordsRecommendedCodesFromResponse()
    {
        SetupEmptyCache();
        SetupSearchWithChunk();
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Zkuste Klidné nožky.")])
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [PostAnswerEnrichmentMiddleware.RecommendedProductCodesKey] = new[] { "MAS009" },
                },
            });

        await CreateHandler().Handle(new AskQuestionRequest { Question = "otoky", TopK = 5 }, default);

        Assert.Equal(["MAS009"], _recorder.RecommendedProductCodes);
    }

    [Fact]
    public async Task Handle_NoRecommendedKey_RecordsEmptyList()
    {
        SetupEmptyCache();
        SetupSearchWithChunk();
        _chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Pijte vodu.")]));

        await CreateHandler().Handle(new AskQuestionRequest { Question = "otoky", TopK = 5 }, default);

        Assert.Empty(_recorder.RecommendedProductCodes);
    }
```

- [ ] **Step 3: Run to verify failure**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep error | head -3
```
Expected: `'PostAnswerEnrichmentMiddleware' does not contain a definition for 'RecommendedProductCodesKey'`.

- [ ] **Step 4: Implement the middleware**

In `PostAnswerEnrichmentMiddleware`, add as the first member of the class:

```csharp
    /// <summary>
    /// <see cref="ChatResponse.AdditionalProperties"/> key under which the catalog codes this middleware
    /// resolved are exposed (string[], known codes only, de-duplicated, first-appearance order). The
    /// middleware is a singleton and cannot reach the scoped interaction recorder, so the caller reads this.
    /// </summary>
    public const string RecommendedProductCodesKey = "anela.recommendedProductCodes";
```

In `GetResponseAsync`, after `var lookup = await _cache.GetProductLookupAsync(cancellationToken);` add:

```csharp
        var resolvedCodes = new List<string>();
```

In the `LinkedProductPattern.Replace` lambda, after the `if (!lookup.TryGetValue(code, out var entry)) return name;` guard, add `resolvedCodes.Add(code);`. In the `BareCodePattern.Replace` lambda, after its `if (!lookup.TryGetValue(code, out var entry)) return match.Value;` guard, add `resolvedCodes.Add(code);`.

Replace the final `return new ChatResponse(...)` block with:

```csharp
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, enriched)])
        {
            ResponseId = response.ResponseId,
            ConversationId = response.ConversationId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            AdditionalProperties = WithRecommendedCodes(response.AdditionalProperties, resolvedCodes),
        };
    }

    // Copies rather than mutates: the inner response's dictionary may be shared with other consumers.
    private static AdditionalPropertiesDictionary WithRecommendedCodes(
        AdditionalPropertiesDictionary? source,
        IReadOnlyList<string> resolvedCodes)
    {
        var copy = new AdditionalPropertiesDictionary();
        if (source is not null)
        {
            foreach (var (key, value) in source)
                copy[key] = value;
        }

        copy[RecommendedProductCodesKey] = resolvedCodes.Distinct(StringComparer.Ordinal).ToArray();
        return copy;
    }
```

(The existing `if (enriched == rawText) return response;` early return stays. A known code is always rewritten, so when no rewrite happened there are no codes to expose.)

- [ ] **Step 5: Implement the handler**

In `AskQuestionHandler.Handle`, after `_recorder.RecordInteraction(RagFeature.KnowledgeBase, request.Question, systemPrompt, answer);` add:

```csharp
        _recorder.RecordRecommendedProducts(ReadRecommendedCodes(response));
```

and add the helper at the bottom of the class:

```csharp
    private static IReadOnlyList<string> ReadRecommendedCodes(ChatResponse response) =>
        response.AdditionalProperties?.TryGetValue(PostAnswerEnrichmentMiddleware.RecommendedProductCodesKey, out var value) == true
        && value is IReadOnlyList<string> codes
            ? codes
            : [];
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~PostAnswerEnrichmentMiddlewareTests|FullyQualifiedName~AskQuestionHandlerTests"
```
Expected: all pass, including every pre-existing middleware test (the rewritten text is unchanged).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs backend/test/Anela.Heblo.Tests/KnowledgeBase
git commit -m "feat: capture recommended product codes from KB answers"
```

---

### Task 6: Repository filters, tag counts, tag library, prompt

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Rag/RagFeedbackLogFilter.cs`, `backend/src/Anela.Heblo.Domain/Features/Rag/RagTagCount.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Rag/IRagInteractionLogRepository.cs`, `backend/src/Anela.Heblo.Persistence/Rag/RagInteractionLogRepository.cs`
- Modify (callers): `App/Features/KnowledgeBase/UseCases/GetFeedbackList/GetFeedbackListHandler.cs`, `App/Features/Smartsupp/UseCases/GetDraftReplyFeedbackList/GetDraftReplyFeedbackListHandler.cs`
- Create: `App/Features/KnowledgeBase/Services/IQuestionTagLibrary.cs`, `App/Features/KnowledgeBase/Services/QuestionTagLibrary.cs`
- Modify: `App/Features/KnowledgeBase/KnowledgeBaseModule.cs`, `App/Features/KnowledgeBase/KnowledgeBaseOptions.cs`, `App/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs`
- Test: `Tests/KnowledgeBase/Integration/RagInteractionLogTagsIntegrationTests.cs` (extend), `Tests/KnowledgeBase/Services/QuestionTagLibraryTests.cs`, `Tests/KnowledgeBase/UseCases/SearchDocumentsHandlerTests.cs`, `Tests/KnowledgeBase/UseCases/GetFeedbackListHandlerTests.cs`, `Tests/Features/Smartsupp/GetDraftReplyFeedbackListHandlerTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record RagFeedbackLogFilter(RagFeature? Feature, bool? HasFeedback = null, string? UserId = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedToExclusive = null, string? ProblemTag = null, string? ProductTag = null)`
  - `public enum RagTagKind { Problem = 0, Product = 1, RecommendedProduct = 2 }`
  - `public sealed record RagTagCount(RagTagKind Kind, string Tag, int Count)`
  - `Task<(List<RagInteractionLog> Logs, int TotalCount)> GetFeedbackLogsPagedAsync(RagFeedbackLogFilter filter, string sortBy, bool sortDescending, int pageNumber, int pageSize, CancellationToken ct = default)`
  - `Task<IReadOnlyList<RagTagCount>> GetTagCountsAsync(RagFeature feature, DateTimeOffset? from, DateTimeOffset? toExclusive, CancellationToken ct = default)`
  - `public interface IQuestionTagLibrary { Task<RagQueryExpansionConfig> BuildExpansionConfigAsync(RagQueryExpansionConfig baseConfig, CancellationToken ct); }`
  - `QuestionTagLibrary` with `public const string Placeholder = "{EXISTING_TAGS}"`, `public const int MaxLibraryTagsPerKind = 200`, `public const string EmptyLibraryText = "(zatím žádné)"`

- [ ] **Step 1: Write the failing repository integration tests** (append to `RagInteractionLogTagsIntegrationTests`)

```csharp
    private async Task SeedAsync(params RagInteractionLog[] logs)
    {
        await using var context = CreateContext();
        context.RagInteractionLogs.AddRange(logs);
        await context.SaveChangesAsync();
    }

    private static readonly DateTimeOffset Day = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetFeedbackLogsPagedAsync_ProblemTagFilter_ReturnsOnlyMatchingRows()
    {
        // Arrange
        await SeedAsync(
            MakeLog(Day, problemTags: ["akné", "suchá pleť"]),
            MakeLog(Day, problemTags: ["růžovka"]),
            MakeLog(Day));
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var (logs, total) = await repo.GetFeedbackLogsPagedAsync(
            new RagFeedbackLogFilter(RagFeature.KnowledgeBase, ProblemTag: "akné"), "CreatedAt", true, 1, 20);

        // Assert
        total.Should().Be(1);
        logs.Single().ProblemTags.Should().Contain("akné");
    }

    [Fact]
    public async Task GetFeedbackLogsPagedAsync_ProductTagFilter_ReturnsOnlyMatchingRows()
    {
        // Arrange
        await SeedAsync(
            MakeLog(Day, productTags: ["hořčíkové máslo"]),
            MakeLog(Day, productTags: ["klidné nožky"]));
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var (logs, total) = await repo.GetFeedbackLogsPagedAsync(
            new RagFeedbackLogFilter(RagFeature.KnowledgeBase, ProductTag: "klidné nožky"), "CreatedAt", true, 1, 20);

        // Assert
        total.Should().Be(1);
        logs.Single().ProductTags.Should().Equal("klidné nožky");
    }

    [Fact]
    public async Task GetFeedbackLogsPagedAsync_DateRange_IsHalfOpen()
    {
        // Arrange
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var toExclusive = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(
            MakeLog(from),                    // included: lower bound inclusive
            MakeLog(toExclusive.AddTicks(-10)), // included: just before the upper bound
            MakeLog(toExclusive),             // excluded: upper bound exclusive
            MakeLog(from.AddTicks(-10)));     // excluded: before the range
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var (_, total) = await repo.GetFeedbackLogsPagedAsync(
            new RagFeedbackLogFilter(RagFeature.KnowledgeBase, CreatedFrom: from, CreatedToExclusive: toExclusive), "CreatedAt", true, 1, 20);

        // Assert
        total.Should().Be(2);
    }

    [Fact]
    public async Task GetTagCountsAsync_CountsPerKindAndScopesToFeatureAndRange()
    {
        // Arrange
        await SeedAsync(
            MakeLog(Day, problemTags: ["akné"], productTags: ["hořčíkové máslo"], recommended: ["MAS009"]),
            MakeLog(Day, problemTags: ["akné", "růžovka"], recommended: ["MAS009", "AKL001"]),
            MakeLog(Day, feature: RagFeature.SmartsuppDraftReply, problemTags: ["akné"]),
            MakeLog(Day.AddDays(-40), problemTags: ["akné"]));
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var counts = await repo.GetTagCountsAsync(RagFeature.KnowledgeBase, Day.AddDays(-1), Day.AddDays(1));

        // Assert
        counts.Should().BeEquivalentTo(new[]
        {
            new RagTagCount(RagTagKind.Problem, "akné", 2),
            new RagTagCount(RagTagKind.Problem, "růžovka", 1),
            new RagTagCount(RagTagKind.Product, "hořčíkové máslo", 1),
            new RagTagCount(RagTagKind.RecommendedProduct, "MAS009", 2),
            new RagTagCount(RagTagKind.RecommendedProduct, "AKL001", 1),
        });
    }

    [Fact]
    public async Task GetTagCountsAsync_NoRange_CountsAllTime()
    {
        // Arrange
        await SeedAsync(MakeLog(Day, problemTags: ["akné"]), MakeLog(Day.AddDays(-400), problemTags: ["akné"]));
        await using var context = CreateContext();

        // Act
        var counts = await new RagInteractionLogRepository(context).GetTagCountsAsync(RagFeature.KnowledgeBase, null, null);

        // Assert
        counts.Should().ContainSingle().Which.Should().Be(new RagTagCount(RagTagKind.Problem, "akné", 2));
    }
```

- [ ] **Step 2: Write the failing tag-library tests**

`Tests/KnowledgeBase/Services/QuestionTagLibraryTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class QuestionTagLibraryTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<ILogger<QuestionTagLibrary>> _logger = new();

    private QuestionTagLibrary Create() => new(_repository.Object, _logger.Object);

    private static RagQueryExpansionConfig Config(string prompt) => new(Enabled: true, Model: "m", Prompt: prompt);

    private void SetupCounts(params RagTagCount[] counts) =>
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(counts);

    [Fact]
    public async Task BuildExpansionConfigAsync_SubstitutesTagsOrderedByCountThenName()
    {
        // Arrange
        SetupCounts(
            new RagTagCount(RagTagKind.Problem, "růžovka", 1),
            new RagTagCount(RagTagKind.Problem, "akné", 3),
            new RagTagCount(RagTagKind.Problem, "ekzém", 1),
            new RagTagCount(RagTagKind.Product, "hořčíkové máslo", 2),
            new RagTagCount(RagTagKind.RecommendedProduct, "MAS009", 9));

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("A\n{EXISTING_TAGS}\nB"), CancellationToken.None);

        // Assert
        result.Prompt.Should().Be("A\nProblémy: akné, ekzém, růžovka\nProdukty: hořčíkové máslo\nB");
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_EmptyLibrary_UsesPlaceholderText()
    {
        // Arrange
        SetupCounts();

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        result.Prompt.Should().Be("Problémy: (zatím žádné)\nProdukty: (zatím žádné)");
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_CapsEachKindAt200()
    {
        // Arrange
        var counts = Enumerable.Range(0, 250)
            .Select(i => new RagTagCount(RagTagKind.Problem, $"tag{i:D3}", 250 - i))
            .ToArray();
        SetupCounts(counts);

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        var problems = result.Prompt.Split('\n')[0]["Problémy: ".Length..].Split(", ");
        problems.Should().HaveCount(QuestionTagLibrary.MaxLibraryTagsPerKind);
        problems.Should().Contain("tag000").And.NotContain("tag200");
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_PromptWithoutPlaceholder_ReturnedUnchangedWithoutQuery()
    {
        // Arrange
        var config = Config("Plain prompt");

        // Act
        var result = await Create().BuildExpansionConfigAsync(config, CancellationToken.None);

        // Assert
        result.Should().Be(config);
        _repository.Verify(
            r => r.GetTagCountsAsync(It.IsAny<RagFeature>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_RepositoryThrows_FallsBackToEmptyLibraryAndLogsError()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("db"));

        // Act
        var result = await Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), CancellationToken.None);

        // Assert
        result.Prompt.Should().Be("Problémy: (zatím žádné)\nProdukty: (zatím žádné)");
        _logger.Verify(
            l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildExpansionConfigAsync_CallerCancelled_Rethrows()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = () => Create().BuildExpansionConfigAsync(Config("{EXISTING_TAGS}"), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 3: Add the tag-library wiring test** (append to `SearchDocumentsHandlerTests`)

First change the test-class fields and `CreateHandler` to inject a pass-through library:

```csharp
    private readonly Mock<IQuestionTagLibrary> _tagLibrary = new();

    public SearchDocumentsHandlerTests()
    {
        _tagLibrary
            .Setup(l => l.BuildExpansionConfigAsync(It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagQueryExpansionConfig c, CancellationToken _) => c);
    }
```

In **both** places the file constructs `SearchDocumentsHandler` (`CreateHandler` and the second construction near line 341), insert `_tagLibrary.Object` after `_expander.Object`:

```csharp
new SearchDocumentsHandler(_embeddingGenerator.Object, _repository.Object, options, _expander.Object, _tagLibrary.Object, _recorder, _logger.Object);
```

Add `using Anela.Heblo.Application.Features.KnowledgeBase.Services;`, then the test:

```csharp
    [Fact]
    public async Task Handle_PassesLibraryEnrichedConfigToExpander()
    {
        // Arrange
        SetupEmbeddingGenerator();
        var enriched = new RagQueryExpansionConfig(true, "m", "ENRICHED PROMPT");
        _tagLibrary
            .Setup(l => l.BuildExpansionConfigAsync(It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(enriched);
        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => RagExpansionResult.Untagged(q));
        _repository.Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default)).ReturnsAsync([]);

        // Act
        await CreateHandler().Handle(new SearchDocumentsRequest { Query = "q", TopK = 5 }, default);

        // Assert
        _expander.Verify(e => e.ExpandAsync("q", enriched, It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 4: Update the two feedback-list handler test mocks to the filter signature**

In `GetFeedbackListHandlerTests.SetupRepository` (and the other `GetFeedbackLogsPagedAsync` setups at lines ~177 and ~210), replace the 8-argument matcher

```csharp
            .Setup(r => r.GetFeedbackLogsPagedAsync(
                It.IsAny<RagFeature?>(),
                It.IsAny<bool?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
```
with
```csharp
            .Setup(r => r.GetFeedbackLogsPagedAsync(
                It.IsAny<RagFeedbackLogFilter>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
```

Any `Verify` or `It.Is<RagFeature?>(f => f == RagFeature.KnowledgeBase)` in those files that asserted the old positional `feature`/`hasFeedback`/`userId` becomes `It.Is<RagFeedbackLogFilter>(f => f.Feature == RagFeature.KnowledgeBase && f.HasFeedback == <same> && f.UserId == <same>)`. Do the same in `Tests/Features/Smartsupp/GetDraftReplyFeedbackListHandlerTests.cs` (lines ~35 and ~71) with `RagFeature.SmartsuppDraftReply`.

- [ ] **Step 5: Run to verify failure**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep error | head -5
```
Expected: errors for missing `RagFeedbackLogFilter`, `RagTagCount`, `IQuestionTagLibrary`, `GetTagCountsAsync`.

- [ ] **Step 6: Add the domain types**

`backend/src/Anela.Heblo.Domain/Features/Rag/RagFeedbackLogFilter.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Rag;

/// <summary>
/// Filter for the RAG feedback/eval list. <see cref="CreatedToExclusive"/> is an exclusive upper bound
/// (half-open range). Tag filters are exact matches against already-normalised tags.
/// </summary>
public sealed record RagFeedbackLogFilter(
    RagFeature? Feature,
    bool? HasFeedback = null,
    string? UserId = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedToExclusive = null,
    string? ProblemTag = null,
    string? ProductTag = null);
```

`backend/src/Anela.Heblo.Domain/Features/Rag/RagTagCount.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Rag;

public enum RagTagKind
{
    Problem = 0,
    Product = 1,
    RecommendedProduct = 2,
}

public sealed record RagTagCount(RagTagKind Kind, string Tag, int Count);
```

- [ ] **Step 7: Change the repository contract and implementation**

In `IRagInteractionLogRepository`, replace the `GetFeedbackLogsPagedAsync` declaration with:

```csharp
    Task<(List<RagInteractionLog> Logs, int TotalCount)> GetFeedbackLogsPagedAsync(
        RagFeedbackLogFilter filter,
        string sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Counts tag occurrences per kind for one feature, optionally within the half-open range
    /// [<paramref name="from"/>, <paramref name="toExclusive"/>).
    /// </summary>
    Task<IReadOnlyList<RagTagCount>> GetTagCountsAsync(
        RagFeature feature,
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        CancellationToken ct = default);
```

In `RagInteractionLogRepository`, replace `GetFeedbackLogsPagedAsync` with:

```csharp
    public async Task<(List<RagInteractionLog> Logs, int TotalCount)> GetFeedbackLogsPagedAsync(
        RagFeedbackLogFilter filter,
        string sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = ApplyFilter(_context.RagInteractionLogs.AsQueryable(), filter);

        query = sortBy switch
        {
            "PrecisionScore" => sortDescending
                ? query.OrderByDescending(l => l.PrecisionScore)
                : query.OrderBy(l => l.PrecisionScore),
            "StyleScore" => sortDescending
                ? query.OrderByDescending(l => l.StyleScore)
                : query.OrderBy(l => l.StyleScore),
            _ => sortDescending
                ? query.OrderByDescending(l => l.CreatedAt)
                : query.OrderBy(l => l.CreatedAt),
        };

        var totalCount = await query.CountAsync(ct);
        var logs = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (logs, totalCount);
    }

    public async Task<IReadOnlyList<RagTagCount>> GetTagCountsAsync(
        RagFeature feature,
        DateTimeOffset? from,
        DateTimeOffset? toExclusive,
        CancellationToken ct = default)
    {
        var filter = new RagFeedbackLogFilter(feature, CreatedFrom: from, CreatedToExclusive: toExclusive);

        // Projects only the three arrays and groups in memory: KB volume is ~120 questions/month, so this
        // stays small, and it avoids depending on EF translating SelectMany+GroupBy over primitive collections.
        var rows = await ApplyFilter(_context.RagInteractionLogs.AsNoTracking(), filter)
            .Select(l => new { l.ProblemTags, l.ProductTags, l.RecommendedProductCodes })
            .ToListAsync(ct);

        return Count(RagTagKind.Problem, rows.SelectMany(r => r.ProblemTags))
            .Concat(Count(RagTagKind.Product, rows.SelectMany(r => r.ProductTags)))
            .Concat(Count(RagTagKind.RecommendedProduct, rows.SelectMany(r => r.RecommendedProductCodes)))
            .ToList();
    }

    private static IEnumerable<RagTagCount> Count(RagTagKind kind, IEnumerable<string> tags) =>
        tags.GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => new RagTagCount(kind, g.Key, g.Count()));

    private static IQueryable<RagInteractionLog> ApplyFilter(IQueryable<RagInteractionLog> query, RagFeedbackLogFilter filter)
    {
        if (filter.Feature.HasValue)
            query = query.Where(l => l.Feature == filter.Feature.Value);

        if (filter.HasFeedback.HasValue)
        {
            query = filter.HasFeedback.Value
                ? query.Where(l => l.PrecisionScore != null || l.StyleScore != null)
                : query.Where(l => l.PrecisionScore == null && l.StyleScore == null);
        }

        if (!string.IsNullOrEmpty(filter.UserId))
            query = query.Where(l => l.UserId == filter.UserId);

        if (filter.CreatedFrom.HasValue)
            query = query.Where(l => l.CreatedAt >= filter.CreatedFrom.Value);

        if (filter.CreatedToExclusive.HasValue)
            query = query.Where(l => l.CreatedAt < filter.CreatedToExclusive.Value);

        if (!string.IsNullOrEmpty(filter.ProblemTag))
            query = query.Where(l => l.ProblemTags.Contains(filter.ProblemTag));

        if (!string.IsNullOrEmpty(filter.ProductTag))
            query = query.Where(l => l.ProductTags.Contains(filter.ProductTag));

        return query;
    }
```

- [ ] **Step 8: Update the two production callers to the filter signature**

`GetFeedbackListHandler.cs`: replace the `GetFeedbackLogsPagedAsync(...)` call with (date and tag fields are added in Task 7):

```csharp
        var (logs, totalCount) = await _repository.GetFeedbackLogsPagedAsync(
            new RagFeedbackLogFilter(RagFeature.KnowledgeBase, request.HasFeedback, request.UserId),
            sortBy,
            request.SortDescending,
            pageNumber,
            pageSize,
            cancellationToken);
```

`GetDraftReplyFeedbackListHandler.cs`: same shape with `RagFeature.SmartsuppDraftReply`.

- [ ] **Step 9: Implement the tag library**

`App/Features/KnowledgeBase/Services/IQuestionTagLibrary.cs`:

```csharp
using Anela.Heblo.Application.Shared.Rag;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Injects the KB's already-used question tags into the expansion prompt so the model reuses them,
/// letting the tag vocabulary build itself. Callable outside the Ask path (e.g. a backfill job).
/// </summary>
public interface IQuestionTagLibrary
{
    Task<RagQueryExpansionConfig> BuildExpansionConfigAsync(RagQueryExpansionConfig baseConfig, CancellationToken ct);
}
```

`App/Features/KnowledgeBase/Services/QuestionTagLibrary.cs`:

```csharp
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Rag;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public sealed class QuestionTagLibrary : IQuestionTagLibrary
{
    public const string Placeholder = "{EXISTING_TAGS}";
    public const int MaxLibraryTagsPerKind = 200;
    public const string EmptyLibraryText = "(zatím žádné)";

    private readonly IRagInteractionLogRepository _repository;
    private readonly ILogger<QuestionTagLibrary> _logger;

    public QuestionTagLibrary(IRagInteractionLogRepository repository, ILogger<QuestionTagLibrary> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<RagQueryExpansionConfig> BuildExpansionConfigAsync(RagQueryExpansionConfig baseConfig, CancellationToken ct)
    {
        if (!baseConfig.Prompt.Contains(Placeholder, StringComparison.Ordinal))
            return baseConfig;

        var counts = await LoadCountsAsync(ct);
        var library = $"Problémy: {Format(counts, RagTagKind.Problem)}\nProdukty: {Format(counts, RagTagKind.Product)}";

        return baseConfig with { Prompt = baseConfig.Prompt.Replace(Placeholder, library, StringComparison.Ordinal) };
    }

    private async Task<IReadOnlyList<RagTagCount>> LoadCountsAsync(CancellationToken ct)
    {
        try
        {
            return await _repository.GetTagCountsAsync(RagFeature.KnowledgeBase, from: null, toExclusive: null, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Tagging still works without the library — the model just gets no reuse guidance this time.
            _logger.LogError(ex, "Failed to load KB question tag library; continuing with an empty library");
            return [];
        }
    }

    private static string Format(IReadOnlyList<RagTagCount> counts, RagTagKind kind)
    {
        var tags = counts
            .Where(c => c.Kind == kind)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Tag, StringComparer.Ordinal)
            .Take(MaxLibraryTagsPerKind)
            .Select(c => c.Tag)
            .ToList();

        return tags.Count == 0 ? EmptyLibraryText : string.Join(", ", tags);
    }
}
```

Register it in `KnowledgeBaseModule.cs` next to the repository registration:

```csharp
        services.AddScoped<IQuestionTagLibrary, QuestionTagLibrary>();
```

(add `using Anela.Heblo.Application.Features.KnowledgeBase.Services;` if missing).

- [ ] **Step 10: Use the library in `SearchDocumentsHandler`**

Add an `IQuestionTagLibrary _tagLibrary` field and a constructor parameter **directly after** `IRagQueryExpander expander` (matching the test construction in Step 3), assign it, and replace the expansion line with:

```csharp
        var expansionConfig = await _tagLibrary.BuildExpansionConfigAsync(_options.ToExpansionConfig(), cancellationToken);
        var expansion = await _expander.ExpandAsync(request.Query, expansionConfig, cancellationToken);
```

- [ ] **Step 11: Replace the KB expansion prompt**

In `KnowledgeBaseOptions` constructor, replace the `QueryExpansionPrompt = """ … """;` literal with:

```csharp
        QueryExpansionPrompt =
            """
            Jsi asistent kosmetické firmy Anela. Zpracuj zákazníkův dotaz a vrať POUZE jeden JSON objekt
            (bez Markdownu, bez jakéhokoli dalšího textu) přesně v tomto tvaru:
            {"expanded": "...", "problemTags": ["..."], "productTags": ["..."]}

            "expanded": přepiš dotaz do strukturovaného formátu vhodného pro sémantické vyhledávání v databázi
            zákaznických konverzací. Uveď POUZE relevantní položky (vynech kategorie bez obsahu), každou na nový řádek:
            Problém: <co zákazník řeší, formuluj jako zákazníkův dotaz>
            Kontext: <typ pleti, stávající rutina, situace>
            Doporučení: <co by mohlo být doporučeno>
            Ingredience: <účinné látky, složky>

            "problemTags": kožní či tělesné problémy a cíle péče, které zákazník řeší (např. akné, suchá pleť,
            otoky nohou). Prázdné pole, pokud jde o provozní dotaz (objednávka, doprava, dostupnost, platba).

            "productTags": pouze produkty, které zákazník v dotazu výslovně jmenuje. Nikdy nepřidávej produkty,
            které bys sám doporučil. Prázdné pole, pokud žádný nejmenuje.

            Pravidla pro štítky: krátká podstatná jména nebo spojení, malými písmeny, v 1. pádě jednotného čísla
            („růžovka", ne „růžovku"), nejvýše 5 v každém poli. Pokud se hodí některý z existujících štítků, použij
            ho přesně v jeho znění; nový vytvoř jen tehdy, když žádný existující nesedí.

            Existující štítky:
            {EXISTING_TAGS}

            Dotaz:
            """;
```

- [ ] **Step 12: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~QuestionTagLibraryTests|FullyQualifiedName~SearchDocumentsHandlerTests|FullyQualifiedName~GetFeedbackListHandlerTests|FullyQualifiedName~GetDraftReplyFeedbackListHandlerTests|FullyQualifiedName~RagInteractionLogTagsIntegrationTests|FullyQualifiedName~KnowledgeBaseRepositoryIntegrationTests"
```
Expected: all pass.

- [ ] **Step 13: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: add self-building KB question tag library and tag-aware repository queries"
```

---

### Task 7: Date and tag filters on the KB feedback list

**Files:**
- Create: `App/Features/KnowledgeBase/Services/PragueDateRange.cs`
- Modify: `App/Features/KnowledgeBase/UseCases/GetFeedbackList/GetFeedbackListRequest.cs`, `GetFeedbackListHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs:93-114`
- Modify: `App/Shared/Rag/RagFeedbackLogSummary.cs`, `App/Shared/Rag/RagFeedbackListMapper.cs`
- Test: `Tests/KnowledgeBase/Services/PragueDateRangeTests.cs`, `Tests/KnowledgeBase/UseCases/GetFeedbackListHandlerTests.cs`

**Interfaces:**
- Consumes: `RagFeedbackLogFilter` (Task 6), `RagExpansionParser.NormalizeTag` (Task 1)
- Produces:
  - `PragueDateRange.StartOfDay(DateOnly? day) → DateTimeOffset?` and `PragueDateRange.EndExclusive(DateOnly? day) → DateTimeOffset?` (UTC offsets)
  - `GetFeedbackListRequest.DateFrom`, `.DateTo` (`DateOnly?`), `.ProblemTag`, `.ProductTag` (`string?`)
  - `RagFeedbackLogSummary.ProblemTags`, `.ProductTags`, `.RecommendedProductCodes` (`List<string>`)
  - `GET /api/KnowledgeBase/feedback/list?dateFrom=yyyy-MM-dd&dateTo=yyyy-MM-dd&problemTag=&productTag=`

- [ ] **Step 1: Write the failing tests**

`Tests/KnowledgeBase/Services/PragueDateRangeTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class PragueDateRangeTests
{
    [Fact]
    public void StartOfDay_SummerTime_IsPreviousDay22Utc()
    {
        PragueDateRange.StartOfDay(new DateOnly(2026, 10, 14))
            .Should().Be(new DateTimeOffset(2026, 10, 13, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void StartOfDay_WinterTime_IsPreviousDay23Utc()
    {
        PragueDateRange.StartOfDay(new DateOnly(2026, 1, 15))
            .Should().Be(new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void EndExclusive_IsStartOfNextPragueDay()
    {
        PragueDateRange.EndExclusive(new DateOnly(2026, 10, 14))
            .Should().Be(new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void EndExclusive_AcrossDstSwitch_UsesNextDaysOffset()
    {
        // 2026-10-25 is the CEST -> CET switch day; the next day starts at 23:00 UTC.
        PragueDateRange.EndExclusive(new DateOnly(2026, 10, 25))
            .Should().Be(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Null_ReturnsNull()
    {
        PragueDateRange.StartOfDay(null).Should().BeNull();
        PragueDateRange.EndExclusive(null).Should().BeNull();
    }
}
```

Append to `GetFeedbackListHandlerTests` (add `using FluentAssertions;` if the file lacks it):

```csharp
    private RagFeedbackLogFilter? _captured;

    private async Task<RagFeedbackLogFilter> SendAndCapture(GetFeedbackListRequest request)
    {
        _repository
            .Setup(r => r.GetFeedbackLogsPagedAsync(It.IsAny<RagFeedbackLogFilter>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<RagFeedbackLogFilter, string, bool, int, int, CancellationToken>((f, _, _, _, _, _) => _captured = f)
            .ReturnsAsync((new List<RagInteractionLog>(), 0));
        _repository
            .Setup(r => r.GetFeedbackStatsAsync(It.IsAny<RagFeature?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultStats());

        await new GetFeedbackListHandler(_repository.Object, _userDisplayNameResolver.Object).Handle(request, default);
        return _captured!;
    }

    [Fact]
    public async Task Handle_SameDayRange_CoversWholePragueDay()
    {
        var filter = await SendAndCapture(new GetFeedbackListRequest
        {
            DateFrom = new DateOnly(2026, 10, 14),
            DateTo = new DateOnly(2026, 10, 14),
        });

        filter.CreatedFrom.Should().Be(new DateTimeOffset(2026, 10, 13, 22, 0, 0, TimeSpan.Zero));
        filter.CreatedToExclusive.Should().Be(new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Handle_ReversedRange_PassesEmptyRangeAndReturnsSuccess()
    {
        var filter = await SendAndCapture(new GetFeedbackListRequest
        {
            DateFrom = new DateOnly(2026, 10, 20),
            DateTo = new DateOnly(2026, 10, 10),
        });

        // from > toExclusive: the repository naturally returns nothing — no exception, no special case.
        filter.CreatedFrom.Should().BeAfter(filter.CreatedToExclusive!.Value);
    }

    [Fact]
    public async Task Handle_NormalisesTagFilters()
    {
        var filter = await SendAndCapture(new GetFeedbackListRequest { ProblemTag = " AKNÉ ", ProductTag = "Hořčíkové   Máslo" });

        filter.ProblemTag.Should().Be("akné");
        filter.ProductTag.Should().Be("hořčíkové máslo");
        filter.Feature.Should().Be(RagFeature.KnowledgeBase);
    }

    [Fact]
    public async Task Handle_BlankTagFilters_BecomeNull()
    {
        var filter = await SendAndCapture(new GetFeedbackListRequest { ProblemTag = "  ", ProductTag = "" });

        filter.ProblemTag.Should().BeNull();
        filter.ProductTag.Should().BeNull();
    }

    [Fact]
    public async Task Handle_MapsTagsOntoSummary()
    {
        var log = MakeLog();
        log.ProblemTags = ["akné"];
        log.ProductTags = ["hořčíkové máslo"];
        log.RecommendedProductCodes = ["MAS009"];
        SetupRepository([log]);

        var result = await new GetFeedbackListHandler(_repository.Object, _userDisplayNameResolver.Object)
            .Handle(new GetFeedbackListRequest(), default);

        result.Logs.Single().ProblemTags.Should().Equal("akné");
        result.Logs.Single().ProductTags.Should().Equal("hořčíkové máslo");
        result.Logs.Single().RecommendedProductCodes.Should().Equal("MAS009");
    }
```

(The file has no `CreateHandler()` helper; its existing tests construct `new GetFeedbackListHandler(_repository.Object, _userDisplayNameResolver.Object)` directly, as above.)

- [ ] **Step 2: Run to verify failure**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep error | head -3
```
Expected: `PragueDateRange` / `DateFrom` / `ProblemTags` not found.

- [ ] **Step 3: Implement `PragueDateRange`**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

/// <summary>
/// Converts inclusive Prague calendar days from the UI into a half-open UTC range
/// [StartOfDay(from), EndExclusive(to)). DST-aware: each bound uses the offset of its own local midnight.
/// </summary>
public static class PragueDateRange
{
    private static readonly TimeZoneInfo Prague = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static DateTimeOffset? StartOfDay(DateOnly? day) =>
        day is null ? null : LocalMidnightUtc(day.Value);

    public static DateTimeOffset? EndExclusive(DateOnly? day) =>
        day is null ? null : LocalMidnightUtc(day.Value.AddDays(1));

    private static DateTimeOffset LocalMidnightUtc(DateOnly day)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(localMidnight, Prague.GetUtcOffset(localMidnight)).ToUniversalTime();
    }
}
```

- [ ] **Step 4: Extend the request, handler, summary and mapper**

`GetFeedbackListRequest`: add after `UserId`:

```csharp
    /// <summary>Inclusive first day (Europe/Prague).</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>Inclusive last day (Europe/Prague).</summary>
    public DateOnly? DateTo { get; set; }

    public string? ProblemTag { get; set; }
    public string? ProductTag { get; set; }
```

`GetFeedbackListHandler`: replace the filter construction from Task 6 with:

```csharp
        var filter = new RagFeedbackLogFilter(
            RagFeature.KnowledgeBase,
            request.HasFeedback,
            request.UserId,
            PragueDateRange.StartOfDay(request.DateFrom),
            PragueDateRange.EndExclusive(request.DateTo),
            RagExpansionParser.NormalizeTag(request.ProblemTag),
            RagExpansionParser.NormalizeTag(request.ProductTag));

        var (logs, totalCount) = await _repository.GetFeedbackLogsPagedAsync(
            filter,
            sortBy,
            request.SortDescending,
            pageNumber,
            pageSize,
            cancellationToken);
```

(add `using Anela.Heblo.Application.Features.KnowledgeBase.Services;`). The stats bar keeps `GetFeedbackStatsAsync(RagFeature.KnowledgeBase, …)` unfiltered. That's existing behaviour, deliberately left alone.

`RagFeedbackLogSummary`: add after `FeedbackComment`:

```csharp
    // Question tags (populated for KnowledgeBase; may also be present on Smartsupp rows)
    public List<string> ProblemTags { get; set; } = [];
    public List<string> ProductTags { get; set; } = [];
    public List<string> RecommendedProductCodes { get; set; } = [];
```

`RagFeedbackListMapper.ToSummary`: add after `FeedbackComment = log.FeedbackComment,`:

```csharp
            ProblemTags = log.ProblemTags.ToList(),
            ProductTags = log.ProductTags.ToList(),
            RecommendedProductCodes = log.RecommendedProductCodes.ToList(),
```

- [ ] **Step 5: Extend the controller action**

In `KnowledgeBaseController.GetFeedbackList`, add parameters after `userId`:

```csharp
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] string? problemTag = null,
        [FromQuery] string? productTag = null,
```

and the matching initialisers after `UserId = userId,`:

```csharp
            DateFrom = dateFrom,
            DateTo = dateTo,
            ProblemTag = problemTag,
            ProductTag = productTag,
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~PragueDateRangeTests|FullyQualifiedName~GetFeedbackListHandlerTests|FullyQualifiedName~GetDraftReplyFeedbackListHandlerTests"
```
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: filter KB feedback list by Prague date range and question tags"
```

---

### Task 8: Tag statistics endpoint

**Files:**
- Create: `App/Features/KnowledgeBase/UseCases/GetQuestionTagStats/GetQuestionTagStatsRequest.cs` (request, response and DTO; follows the `GetFeedbackListRequest.cs` convention of request + response in one file)
- Create: `App/Features/KnowledgeBase/UseCases/GetQuestionTagStats/GetQuestionTagStatsHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`
- Test: `Tests/KnowledgeBase/UseCases/GetQuestionTagStatsHandlerTests.cs`

**Interfaces:**
- Consumes: `IRagInteractionLogRepository.GetTagCountsAsync`, `RagTagKind`, `RagTagCount` (Task 6); `PragueDateRange` (Task 7); `IProductEnrichmentCache.GetProductLookupAsync` (existing, returns `IReadOnlyDictionary<string, ProductEnrichmentEntry>`)
- Produces:
  - `class QuestionTagCountDto { string Tag; string Label; int Count }`
  - `class GetQuestionTagStatsResponse : BaseResponse { List<QuestionTagCountDto> ProblemTags; ProductTags; RecommendedProducts }`
  - `GET /api/KnowledgeBase/feedback/tag-stats?dateFrom=yyyy-MM-dd&dateTo=yyyy-MM-dd`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetQuestionTagStats;
using Anela.Heblo.Domain.Features.Rag;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class GetQuestionTagStatsHandlerTests
{
    private readonly Mock<IRagInteractionLogRepository> _repository = new();
    private readonly Mock<IProductEnrichmentCache> _cache = new();

    private GetQuestionTagStatsHandler Create() => new(_repository.Object, _cache.Object);

    private void SetupCache(params (string code, string name)[] products) =>
        _cache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products.ToDictionary(
                p => p.code,
                p => new ProductEnrichmentEntry { ProductCode = p.code, ProductName = p.name }));

    [Fact]
    public async Task Handle_GroupsByKindOrderedByCountThenTag()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagTagCount(RagTagKind.Problem, "růžovka", 2),
                new RagTagCount(RagTagKind.Problem, "akné", 5),
                new RagTagCount(RagTagKind.Problem, "ekzém", 2),
                new RagTagCount(RagTagKind.Product, "hořčíkové máslo", 1),
            });
        SetupCache();

        // Act
        var result = await Create().Handle(new GetQuestionTagStatsRequest(), default);

        // Assert
        result.Success.Should().BeTrue();
        result.ProblemTags.Select(t => (t.Tag, t.Count)).Should().Equal(("akné", 5), ("ekzém", 2), ("růžovka", 2));
        result.ProblemTags.Should().OnlyContain(t => t.Label == t.Tag);
        result.ProductTags.Select(t => t.Tag).Should().Equal("hořčíkové máslo");
        result.RecommendedProducts.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RecommendedProducts_ResolvesNameAndFallsBackToCode()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(RagFeature.KnowledgeBase, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RagTagCount(RagTagKind.RecommendedProduct, "MAS009", 3),
                new RagTagCount(RagTagKind.RecommendedProduct, "GONE01", 1),
            });
        SetupCache(("MAS009", "Klidné nožky balzám"));

        // Act
        var result = await Create().Handle(new GetQuestionTagStatsRequest(), default);

        // Assert
        result.RecommendedProducts.Select(p => (p.Tag, p.Label, p.Count))
            .Should().Equal(("MAS009", "Klidné nožky balzám", 3), ("GONE01", "GONE01", 1));
    }

    [Fact]
    public async Task Handle_ConvertsPragueDaysToHalfOpenUtcRange()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(It.IsAny<RagFeature>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RagTagCount>());
        SetupCache();

        // Act
        await Create().Handle(new GetQuestionTagStatsRequest { DateFrom = new DateOnly(2026, 10, 1), DateTo = new DateOnly(2026, 10, 31) }, default);

        // Assert
        _repository.Verify(r => r.GetTagCountsAsync(
            RagFeature.KnowledgeBase,
            new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NoRecommended_DoesNotLoadProductCatalog()
    {
        // Arrange
        _repository
            .Setup(r => r.GetTagCountsAsync(It.IsAny<RagFeature>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new RagTagCount(RagTagKind.Problem, "akné", 1) });

        // Act
        await Create().Handle(new GetQuestionTagStatsRequest(), default);

        // Assert
        _cache.Verify(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep error | head -3
```
Expected: `GetQuestionTagStatsHandler` not found.

- [ ] **Step 3: Implement the request, response and DTO**

`GetQuestionTagStatsRequest.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetQuestionTagStats;

public class GetQuestionTagStatsRequest : IRequest<GetQuestionTagStatsResponse>
{
    /// <summary>Inclusive first day (Europe/Prague). Null = no lower bound.</summary>
    public DateOnly? DateFrom { get; set; }

    /// <summary>Inclusive last day (Europe/Prague). Null = no upper bound.</summary>
    public DateOnly? DateTo { get; set; }
}

public class QuestionTagCountDto
{
    /// <summary>The tag value used for filtering (a normalised tag, or a product code for recommended products).</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Display text: the tag itself, or the catalog product name for recommended products.</summary>
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }
}

public class GetQuestionTagStatsResponse : BaseResponse
{
    public List<QuestionTagCountDto> ProblemTags { get; set; } = [];
    public List<QuestionTagCountDto> ProductTags { get; set; } = [];
    public List<QuestionTagCountDto> RecommendedProducts { get; set; } = [];
}
```

- [ ] **Step 4: Implement the handler**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.Rag;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetQuestionTagStats;

public class GetQuestionTagStatsHandler : IRequestHandler<GetQuestionTagStatsRequest, GetQuestionTagStatsResponse>
{
    private readonly IRagInteractionLogRepository _repository;
    private readonly IProductEnrichmentCache _productCache;

    public GetQuestionTagStatsHandler(IRagInteractionLogRepository repository, IProductEnrichmentCache productCache)
    {
        _repository = repository;
        _productCache = productCache;
    }

    public async Task<GetQuestionTagStatsResponse> Handle(GetQuestionTagStatsRequest request, CancellationToken cancellationToken)
    {
        var counts = await _repository.GetTagCountsAsync(
            RagFeature.KnowledgeBase,
            PragueDateRange.StartOfDay(request.DateFrom),
            PragueDateRange.EndExclusive(request.DateTo),
            cancellationToken);

        var recommended = OfKind(counts, RagTagKind.RecommendedProduct);
        IReadOnlyDictionary<string, ProductEnrichmentEntry> productNames = recommended.Count == 0
            ? new Dictionary<string, ProductEnrichmentEntry>()
            : await _productCache.GetProductLookupAsync(cancellationToken);

        return new GetQuestionTagStatsResponse
        {
            ProblemTags = ToDtos(OfKind(counts, RagTagKind.Problem), tag => tag),
            ProductTags = ToDtos(OfKind(counts, RagTagKind.Product), tag => tag),
            RecommendedProducts = ToDtos(recommended, code => productNames.TryGetValue(code, out var p) ? p.ProductName : code),
        };
    }

    private static List<RagTagCount> OfKind(IReadOnlyList<RagTagCount> counts, RagTagKind kind) =>
        counts
            .Where(c => c.Kind == kind)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Tag, StringComparer.Ordinal)
            .ToList();

    private static List<QuestionTagCountDto> ToDtos(IEnumerable<RagTagCount> counts, Func<string, string> label) =>
        counts.Select(c => new QuestionTagCountDto { Tag = c.Tag, Label = label(c.Tag), Count = c.Count }).ToList();
}
```

(The explicit `IReadOnlyDictionary` type is required: `GetProductLookupAsync` returns `IReadOnlyDictionary<…>`, and with `var` the two ternary branches have no common type.)

- [ ] **Step 5: Add the endpoint** (in `KnowledgeBaseController`, directly after `GetFeedbackList`)

```csharp
    [HttpGet("feedback/tag-stats")]
    [FeatureAuthorize(Feature.Customer_KnowledgeBase, AccessLevel.Write)]
    public async Task<ActionResult<GetQuestionTagStatsResponse>> GetQuestionTagStats(
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetQuestionTagStatsRequest
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
        }, ct);
        return HandleResponse(result);
    }
```

(add `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetQuestionTagStats;`).

- [ ] **Step 6: Run the tests (including the BaseResponse contract test) to verify they pass**

```bash
dotnet build backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~GetQuestionTagStatsHandlerTests|FullyQualifiedName~BaseResponse|FullyQualifiedName~Contract"
```
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add backend/src backend/test
git commit -m "feat: add KB question tag statistics endpoint"
```

---

### Task 9: Frontend data layer

**Files:**
- Regenerate: `frontend/src/api/generated/api-client.ts`
- Modify: `frontend/src/api/hooks/useKnowledgeBase.ts`
- Modify: `frontend/src/components/feedback/ragFeedbackTypes.ts`, `frontend/src/components/feedback/ragFeedbackMapping.ts`
- Modify: `frontend/src/components/feedback/types.ts`, `frontend/src/components/feedback/adapters/useKbFeedbackAdapter.ts`
- Test: `frontend/src/components/feedback/adapters/__tests__/useKbFeedbackAdapter.test.ts`, `frontend/src/api/hooks/__tests__/useKnowledgeBaseFeedback.test.ts` (new)

**Interfaces:**
- Consumes: `GET /api/KnowledgeBase/feedback/list` with the new params (Task 7), `GET /api/KnowledgeBase/feedback/tag-stats` (Task 8)
- Produces:
  - `GetFeedbackListParams` extended with `dateFrom?: string; dateTo?: string; problemTag?: string; productTag?: string;` (dates as `yyyy-MM-dd`)
  - `buildFeedbackListQuery(params: GetFeedbackListParams): string` (exported for testing)
  - `useKnowledgeBaseTagStatsQuery(params: { dateFrom?: string; dateTo?: string })` returning `{ problemTags: TagCount[]; productTags: TagCount[]; recommendedProducts: TagCount[] }`
  - `export interface TagCount { tag: string; label: string; count: number }` (in `ragFeedbackTypes.ts`)
  - `RagFeedbackLogSummary` gains `problemTags: string[]; productTags: string[]; recommendedProductCodes: string[]`
  - `FeedbackRow` gains `tags?: string[]`
  - `export interface KbFeedbackParams extends GenericFeedbackParams { dateFrom?: string; dateTo?: string; problemTag?: string; productTag?: string }` (in `types.ts`)
  - `useKbFeedbackAdapter(params: KbFeedbackParams)`; each row's `tags` = problem tags followed by product tags

- [ ] **Step 1: Regenerate the API client**

```bash
cd backend/src/Anela.Heblo.API && dotnet msbuild -t:GenerateFrontendClientManual && cd -
grep -n "class QuestionTagCountDto\|knowledgeBase_GetQuestionTagStats\|problemTags?: string\[\]" frontend/src/api/generated/api-client.ts | head
```
Expected: `QuestionTagCountDto`, `knowledgeBase_GetQuestionTagStats`, and `problemTags?: string[]` on `RagFeedbackLogSummary` all appear. The generated `knowledgeBase_GetFeedbackList` now takes `dateFrom: Date | …` and serializes it with `toISOString()`. **That's the trap; the hooks below must not call it with dates.**

- [ ] **Step 2: Write the failing tests**

`frontend/src/api/hooks/__tests__/useKnowledgeBaseFeedback.test.ts`:

```ts
import { buildFeedbackListQuery } from '../useKnowledgeBase';

describe('buildFeedbackListQuery', () => {
  test('sends dates as plain yyyy-MM-dd, never ISO timestamps', () => {
    const qs = buildFeedbackListQuery({ dateFrom: '2026-10-01', dateTo: '2026-10-14' });
    expect(qs).toContain('dateFrom=2026-10-01');
    expect(qs).toContain('dateTo=2026-10-14');
    expect(qs).not.toContain('T00');
  });

  test('omits undefined and empty values', () => {
    const qs = buildFeedbackListQuery({ pageNumber: 2, problemTag: '', productTag: undefined });
    expect(qs).toBe('pageNumber=2');
  });

  test('encodes Czech tag values', () => {
    const qs = buildFeedbackListQuery({ problemTag: 'suchá pleť' });
    expect(new URLSearchParams(qs).get('problemTag')).toBe('suchá pleť');
  });

  test('keeps boolean false (hasFeedback=false is a real filter)', () => {
    expect(buildFeedbackListQuery({ hasFeedback: false })).toBe('hasFeedback=false');
  });
});
```

Append to `useKbFeedbackAdapter.test.ts`:

```ts
test('passes date and tag filters to the list query', () => {
  const spy = jest.spyOn(kbHooks, 'useKnowledgeBaseFeedbackListQuery');
  renderHook(() =>
    useKbFeedbackAdapter({ ...params, dateFrom: '2026-10-01', dateTo: '2026-10-14', problemTag: 'akné', productTag: 'hořčíkové máslo' }),
  );
  expect(spy).toHaveBeenCalledWith(
    expect.objectContaining({ dateFrom: '2026-10-01', dateTo: '2026-10-14', problemTag: 'akné', productTag: 'hořčíkové máslo' }),
  );
});

test('maps problem then product tags onto the row', () => {
  jest.spyOn(kbHooks, 'useKnowledgeBaseFeedbackListQuery').mockReturnValue({
    data: {
      success: true,
      logs: [{ ...mockLog, problemTags: ['akné'], productTags: ['hořčíkové máslo'], recommendedProductCodes: ['MAS009'] }],
      totalCount: 1, pageNumber: 1, pageSize: 20, totalPages: 1, stats: mockStats,
    },
    isLoading: false,
    isError: false,
  } as any);
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].tags).toEqual(['akné', 'hořčíkové máslo']);
});

test('row tags default to empty when the log has none', () => {
  const { result } = renderHook(() => useKbFeedbackAdapter(params));
  expect(result.current.rows[0].tags).toEqual([]);
});
```

- [ ] **Step 3: Run to verify failure**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false useKnowledgeBaseFeedback useKbFeedbackAdapter; cd -
```
Expected: FAIL. `buildFeedbackListQuery` is not exported, and `tags` is undefined.

- [ ] **Step 4: Implement the types and mapping**

`ragFeedbackTypes.ts`: add to `RagFeedbackLogSummary` after `hasFeedback: boolean;`:

```ts
  // Question tags (KnowledgeBase)
  problemTags: string[];
  productTags: string[];
  recommendedProductCodes: string[];
```

and append:

```ts
export interface TagCount {
  tag: string;
  label: string;
  count: number;
}
```

`ragFeedbackMapping.ts`: in `toLocalFeedbackListResponse`'s log mapping, add after `hasFeedback: log.hasFeedback ?? false,`:

```ts
    problemTags: log.problemTags ?? [],
    productTags: log.productTags ?? [],
    recommendedProductCodes: log.recommendedProductCodes ?? [],
```

`types.ts`: add `tags?: string[];` to `FeedbackRow` (after `hasFeedback: boolean;`), and append:

```ts
export interface KbFeedbackParams extends GenericFeedbackParams {
  /** Inclusive first day, yyyy-MM-dd (Europe/Prague). */
  dateFrom?: string;
  /** Inclusive last day, yyyy-MM-dd (Europe/Prague). */
  dateTo?: string;
  problemTag?: string;
  productTag?: string;
}
```

- [ ] **Step 5: Implement the hooks**

In `useKnowledgeBase.ts`, extend `GetFeedbackListParams`:

```ts
export interface GetFeedbackListParams {
  pageNumber?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
  hasFeedback?: boolean;
  userId?: string;
  /** yyyy-MM-dd */
  dateFrom?: string;
  /** yyyy-MM-dd */
  dateTo?: string;
  problemTag?: string;
  productTag?: string;
}
```

Add the query-key entry to `knowledgeBaseKeys`:

```ts
  tagStats: (params: TagStatsParams) =>
    [...QUERY_KEYS.knowledgeBase, 'tag-stats', params] as const,
```

Add the helpers and replace `useKnowledgeBaseFeedbackListQuery`:

```ts
export interface TagStatsParams {
  /** yyyy-MM-dd */
  dateFrom?: string;
  /** yyyy-MM-dd */
  dateTo?: string;
}

export interface QuestionTagStats {
  problemTags: TagCount[];
  productTags: TagCount[];
  recommendedProducts: TagCount[];
}

// The generated client serializes DateOnly query params with toISOString(), which ASP.NET won't bind
// to DateOnly (and which is a day early for a local-midnight Date in CEST). Build the query by hand.
export const buildFeedbackListQuery = (params: GetFeedbackListParams | TagStatsParams): string => {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return;
    search.append(key, String(value));
  });
  return search.toString();
};

const fetchKnowledgeBaseJson = async (relativeUrl: string): Promise<unknown> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`HTTP error! status: ${response.status}`);
  }
  return response.json();
};

export const useKnowledgeBaseFeedbackListQuery = (params: GetFeedbackListParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.feedbackList(params),
    queryFn: async (): Promise<GetFeedbackListResponse> => {
      const query = buildFeedbackListQuery(params);
      const json = await fetchKnowledgeBaseJson(`/api/KnowledgeBase/feedback/list${query ? `?${query}` : ''}`);
      // fromJS revives createdAt/sentAt into Date objects, which toLocalFeedbackListResponse expects.
      return toLocalFeedbackListResponse(GeneratedGetFeedbackListResponse.fromJS(json));
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};

export const useKnowledgeBaseTagStatsQuery = (params: TagStatsParams = {}) => {
  return useQuery({
    queryKey: knowledgeBaseKeys.tagStats(params),
    queryFn: async (): Promise<QuestionTagStats> => {
      const query = buildFeedbackListQuery(params);
      const json = (await fetchKnowledgeBaseJson(
        `/api/KnowledgeBase/feedback/tag-stats${query ? `?${query}` : ''}`,
      )) as Partial<Record<keyof QuestionTagStats, Partial<TagCount>[]>>;
      const toTagCounts = (items?: Partial<TagCount>[]): TagCount[] =>
        (items ?? []).map((i) => ({ tag: i.tag ?? '', label: i.label ?? i.tag ?? '', count: i.count ?? 0 }));
      return {
        problemTags: toTagCounts(json.problemTags),
        productTags: toTagCounts(json.productTags),
        recommendedProducts: toTagCounts(json.recommendedProducts),
      };
    },
    staleTime: 2 * 60 * 1000,
    gcTime: 5 * 60 * 1000,
  });
};
```

Add the imports at the top of the file:

```ts
import { GetFeedbackListResponse as GeneratedGetFeedbackListResponse } from '../generated/api-client';
import type { TagCount } from '../../components/feedback/ragFeedbackTypes';
```

If the file already imports a generated `GetFeedbackListResponse` under that name, reuse that import instead of adding the alias.

- [ ] **Step 6: Implement the adapter**

In `useKbFeedbackAdapter.ts`, change the signature and query params:

```ts
import type { FeedbackDetail, GenericFeedbackStats, KbFeedbackParams } from '../types';

export function useKbFeedbackAdapter(params: KbFeedbackParams) {
  const query = useKnowledgeBaseFeedbackListQuery({
    pageNumber: params.pageNumber,
    pageSize: params.pageSize,
    sortBy: params.sortBy,
    sortDescending: params.sortDescending,
    hasFeedback: params.hasFeedback,
    userId: params.userId,
    dateFrom: params.dateFrom,
    dateTo: params.dateTo,
    problemTag: params.problemTag,
    productTag: params.productTag,
  });
```

and in the row mapping add after `feedbackComment: log.feedbackComment,`:

```ts
    tags: [...(log.problemTags ?? []), ...(log.productTags ?? [])],
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false useKnowledgeBaseFeedback useKbFeedbackAdapter ragFeedback; cd -
```
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/api frontend/src/components/feedback
git commit -m "feat: KB feedback hooks for date/tag filters and tag stats"
```

---

### Task 10: Frontend filters, frequency panel and tag chips

**Files:**
- Create: `frontend/src/components/feedback/TagChips.tsx`, `KbQuestionTagFilters.tsx`, `KbTagFrequencyPanel.tsx`
- Create tests: `frontend/src/components/feedback/__tests__/TagChips.test.tsx`, `KbQuestionTagFilters.test.tsx`, `KbTagFrequencyPanel.test.tsx`
- Modify: `frontend/src/components/feedback/GenericFeedbackTable.tsx`, `frontend/src/components/feedback/RagFeedbackDetailExtra.tsx`, `frontend/src/pages/MarketingFeedbackPage.tsx`
- Test: `frontend/src/pages/__tests__/MarketingFeedbackPage.test.tsx`

**Interfaces:**
- Consumes: `useKnowledgeBaseTagStatsQuery`, `TagCount`, `KbFeedbackParams`, `FeedbackRow.tags` (Task 9)
- Produces:
  - `TagChips({ tags }: { tags: string[] })`: renders nothing for an empty list
  - `KbQuestionTagFilters` props: `{ dateFrom?: string; dateTo?: string; problemTag?: string; productTag?: string; problemOptions: TagCount[]; productOptions: TagCount[]; onChange: (update: Partial<KbFeedbackParams>) => void }`
  - `KbTagFrequencyPanel` props: `{ stats?: QuestionTagStats; isLoading: boolean; onProblemClick: (tag: string) => void; onProductClick: (tag: string) => void }`

Before writing UI code, read `docs/design/ui_design_document.md` and `docs/design/layout_definition.md`, and match the classes already used in `GenericFeedbackFilters.tsx` (`selectClass`, label styles, dark-mode `graphite-*` tokens).

- [ ] **Step 1: Write the failing component tests**

`__tests__/TagChips.test.tsx`:

```tsx
import React from 'react';
import { render, screen } from '@testing-library/react';
import TagChips from '../TagChips';

test('renders one chip per tag', () => {
  render(<TagChips tags={['akné', 'hořčíkové máslo']} />);
  expect(screen.getByText('akné')).toBeInTheDocument();
  expect(screen.getByText('hořčíkové máslo')).toBeInTheDocument();
});

test('renders nothing when there are no tags', () => {
  const { container } = render(<TagChips tags={[]} />);
  expect(container).toBeEmptyDOMElement();
});
```

`__tests__/KbQuestionTagFilters.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import KbQuestionTagFilters from '../KbQuestionTagFilters';

const problemOptions = [{ tag: 'akné', label: 'akné', count: 5 }, { tag: 'růžovka', label: 'růžovka', count: 2 }];
const productOptions = [{ tag: 'hořčíkové máslo', label: 'hořčíkové máslo', count: 1 }];

const renderFilters = (onChange = jest.fn(), props: Partial<React.ComponentProps<typeof KbQuestionTagFilters>> = {}) => {
  render(<KbQuestionTagFilters problemOptions={problemOptions} productOptions={productOptions} onChange={onChange} {...props} />);
  return onChange;
};

test('shows options with counts', () => {
  renderFilters();
  expect(screen.getByRole('option', { name: 'akné (5)' })).toBeInTheDocument();
  expect(screen.getByRole('option', { name: 'hořčíkové máslo (1)' })).toBeInTheDocument();
});

test('selecting a problem emits problemTag', () => {
  const onChange = renderFilters();
  fireEvent.change(screen.getByLabelText('Problém:'), { target: { value: 'růžovka' } });
  expect(onChange).toHaveBeenCalledWith({ problemTag: 'růžovka' });
});

test('choosing "Vše" clears the tag', () => {
  const onChange = renderFilters(jest.fn(), { problemTag: 'akné' });
  fireEvent.change(screen.getByLabelText('Problém:'), { target: { value: '' } });
  expect(onChange).toHaveBeenCalledWith({ problemTag: undefined });
});

test('date inputs emit yyyy-MM-dd strings and clear to undefined', () => {
  const onChange = renderFilters();
  fireEvent.change(screen.getByLabelText('Od:'), { target: { value: '2026-10-01' } });
  expect(onChange).toHaveBeenCalledWith({ dateFrom: '2026-10-01' });
  fireEvent.change(screen.getByLabelText('Do:'), { target: { value: '' } });
  expect(onChange).toHaveBeenCalledWith({ dateTo: undefined });
});

test('keeps a selected tag visible even when it is not in the current range options', () => {
  renderFilters(jest.fn(), { problemTag: 'ekzém' });
  expect(screen.getByLabelText('Problém:')).toHaveValue('ekzém');
});
```

`__tests__/KbTagFrequencyPanel.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import KbTagFrequencyPanel from '../KbTagFrequencyPanel';

const stats = {
  problemTags: [{ tag: 'akné', label: 'akné', count: 5 }],
  productTags: [{ tag: 'hořčíkové máslo', label: 'hořčíkové máslo', count: 2 }],
  recommendedProducts: [{ tag: 'MAS009', label: 'Klidné nožky balzám', count: 3 }],
};

test('lists each kind with counts', () => {
  render(<KbTagFrequencyPanel stats={stats} isLoading={false} onProblemClick={jest.fn()} onProductClick={jest.fn()} />);
  expect(screen.getByRole('button', { name: /akné/ })).toHaveTextContent('5');
  expect(screen.getByText('Klidné nožky balzám')).toBeInTheDocument();
});

test('clicking a problem or asked-about product calls its handler', () => {
  const onProblemClick = jest.fn();
  const onProductClick = jest.fn();
  render(<KbTagFrequencyPanel stats={stats} isLoading={false} onProblemClick={onProblemClick} onProductClick={onProductClick} />);
  fireEvent.click(screen.getByRole('button', { name: /akné/ }));
  fireEvent.click(screen.getByRole('button', { name: /hořčíkové máslo/ }));
  expect(onProblemClick).toHaveBeenCalledWith('akné');
  expect(onProductClick).toHaveBeenCalledWith('hořčíkové máslo');
});

test('recommended products are not clickable filters', () => {
  render(<KbTagFrequencyPanel stats={stats} isLoading={false} onProblemClick={jest.fn()} onProductClick={jest.fn()} />);
  expect(screen.queryByRole('button', { name: /Klidné nožky balzám/ })).not.toBeInTheDocument();
});

test('shows an empty-state message when no tags exist yet', () => {
  render(
    <KbTagFrequencyPanel
      stats={{ problemTags: [], productTags: [], recommendedProducts: [] }}
      isLoading={false}
      onProblemClick={jest.fn()}
      onProductClick={jest.fn()}
    />,
  );
  expect(screen.getByText('Zatím žádné štítky v tomto období.')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run to verify failure**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false TagChips KbQuestionTagFilters KbTagFrequencyPanel; cd -
```
Expected: FAIL with `Cannot find module '../TagChips'` (and the other two).

- [ ] **Step 3: Implement `TagChips.tsx`**

```tsx
import React from 'react';

interface TagChipsProps {
  tags: string[];
}

function TagChips({ tags }: TagChipsProps) {
  if (tags.length === 0) return null;

  return (
    <div className="flex flex-wrap gap-1">
      {tags.map((tag) => (
        <span
          key={tag}
          className="inline-flex items-center px-2 py-0.5 rounded-full text-xs bg-blue-50 text-blue-700 dark:bg-graphite-surface-2 dark:text-graphite-text"
        >
          {tag}
        </span>
      ))}
    </div>
  );
}

export default TagChips;
```

- [ ] **Step 4: Implement `KbQuestionTagFilters.tsx`**

```tsx
import React from 'react';
import type { TagCount } from './ragFeedbackTypes';
import type { KbFeedbackParams } from './types';

interface KbQuestionTagFiltersProps {
  dateFrom?: string;
  dateTo?: string;
  problemTag?: string;
  productTag?: string;
  problemOptions: TagCount[];
  productOptions: TagCount[];
  onChange: (update: Partial<KbFeedbackParams>) => void;
}

const controlClass =
  'border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text rounded-md text-sm px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-blue-500';
const labelClass = 'text-sm text-gray-600 dark:text-graphite-muted whitespace-nowrap';

const emptyToUndefined = (value: string): string | undefined => (value === '' ? undefined : value);

// A selected tag must stay selectable even if the chosen date range no longer contains it.
const withSelected = (options: TagCount[], selected?: string): TagCount[] =>
  selected && !options.some((o) => o.tag === selected)
    ? [...options, { tag: selected, label: selected, count: 0 }]
    : options;

interface TagSelectProps {
  id: string;
  label: string;
  value?: string;
  options: TagCount[];
  onSelect: (tag: string | undefined) => void;
}

function TagSelect({ id, label, value, options, onSelect }: TagSelectProps) {
  return (
    <div className="flex items-center gap-2">
      <label htmlFor={id} className={labelClass}>{label}</label>
      <select id={id} value={value ?? ''} onChange={(e) => onSelect(emptyToUndefined(e.target.value))} className={controlClass}>
        <option value="">Vše</option>
        {withSelected(options, value).map((o) => (
          <option key={o.tag} value={o.tag}>{`${o.label} (${o.count})`}</option>
        ))}
      </select>
    </div>
  );
}

function KbQuestionTagFilters({
  dateFrom,
  dateTo,
  problemTag,
  productTag,
  problemOptions,
  productOptions,
  onChange,
}: KbQuestionTagFiltersProps) {
  return (
    <div className="flex flex-wrap gap-3 items-center">
      <div className="flex items-center gap-2">
        <label htmlFor="kb-filter-date-from" className={labelClass}>Od:</label>
        <input id="kb-filter-date-from" type="date" value={dateFrom ?? ''} className={controlClass}
          onChange={(e) => onChange({ dateFrom: emptyToUndefined(e.target.value) })} />
      </div>
      <div className="flex items-center gap-2">
        <label htmlFor="kb-filter-date-to" className={labelClass}>Do:</label>
        <input id="kb-filter-date-to" type="date" value={dateTo ?? ''} className={controlClass}
          onChange={(e) => onChange({ dateTo: emptyToUndefined(e.target.value) })} />
      </div>
      <TagSelect id="kb-filter-problem" label="Problém:" value={problemTag} options={problemOptions}
        onSelect={(tag) => onChange({ problemTag: tag })} />
      <TagSelect id="kb-filter-product" label="Produkt:" value={productTag} options={productOptions}
        onSelect={(tag) => onChange({ productTag: tag })} />
    </div>
  );
}

export default KbQuestionTagFilters;
```

- [ ] **Step 5: Implement `KbTagFrequencyPanel.tsx`**

```tsx
import React from 'react';
import type { QuestionTagStats } from '../../api/hooks/useKnowledgeBase';
import type { TagCount } from './ragFeedbackTypes';

const TOP_TAGS_SHOWN = 10;

interface KbTagFrequencyPanelProps {
  stats?: QuestionTagStats;
  isLoading: boolean;
  onProblemClick: (tag: string) => void;
  onProductClick: (tag: string) => void;
}

interface TagColumnProps {
  title: string;
  items: TagCount[];
  onClick?: (tag: string) => void;
}

function TagColumn({ title, items, onClick }: TagColumnProps) {
  return (
    <div className="min-w-0">
      <h3 className="text-xs font-semibold uppercase text-gray-500 dark:text-graphite-muted mb-2">{title}</h3>
      {items.length === 0 ? (
        <p className="text-sm text-gray-400 dark:text-graphite-muted">—</p>
      ) : (
        <ul className="space-y-1">
          {items.slice(0, TOP_TAGS_SHOWN).map((item) => {
            const content = (
              <>
                <span className="truncate">{item.label}</span>
                <span className="ml-2 text-gray-500 dark:text-graphite-muted tabular-nums">{item.count}</span>
              </>
            );
            return (
              <li key={item.tag}>
                {onClick ? (
                  <button type="button" onClick={() => onClick(item.tag)}
                    className="w-full flex justify-between text-left text-sm text-blue-700 hover:underline dark:text-graphite-accent">
                    {content}
                  </button>
                ) : (
                  <div className="flex justify-between text-sm text-gray-800 dark:text-graphite-text">{content}</div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

function KbTagFrequencyPanel({ stats, isLoading, onProblemClick, onProductClick }: KbTagFrequencyPanelProps) {
  if (isLoading) {
    return <div className="text-sm text-gray-500 dark:text-graphite-muted">Načítám štítky…</div>;
  }

  const isEmpty =
    !stats || (stats.problemTags.length === 0 && stats.productTags.length === 0 && stats.recommendedProducts.length === 0);
  if (isEmpty) {
    return <div className="text-sm text-gray-500 dark:text-graphite-muted">Zatím žádné štítky v tomto období.</div>;
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-4 rounded-lg border border-gray-200 dark:border-graphite-border p-4">
      <TagColumn title="Nejčastější problémy" items={stats.problemTags} onClick={onProblemClick} />
      <TagColumn title="Produkty v dotazech" items={stats.productTags} onClick={onProductClick} />
      <TagColumn title="Doporučené produkty" items={stats.recommendedProducts} />
    </div>
  );
}

export default KbTagFrequencyPanel;
```

- [ ] **Step 6: Run the component tests to verify they pass**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false TagChips KbQuestionTagFilters KbTagFrequencyPanel; cd -
```
Expected: PASS.

- [ ] **Step 7: Write the failing page tests** (append to `MarketingFeedbackPage.test.tsx`)

The page now calls `useKnowledgeBaseTagStatsQuery`, so mock it at the top of the file, next to the other `jest.mock` calls. Shell-component tests must mock every new context or hook dependency:

```tsx
import * as kbHooks from '../../api/hooks/useKnowledgeBase';
jest.mock('../../api/hooks/useKnowledgeBase');

const tagStats = {
  problemTags: [{ tag: 'akné', label: 'akné', count: 5 }],
  productTags: [],
  recommendedProducts: [],
};
```

In the existing `setupMocks` helper add:

```tsx
  jest.spyOn(kbHooks, 'useKnowledgeBaseTagStatsQuery').mockReturnValue({ data: tagStats, isLoading: false } as any);
```

Then the tests:

```tsx
test('KB tab shows the tag filters and frequency panel', () => {
  setupMocks({ hasKb: true });
  render(<MarketingFeedbackPage />);
  expect(screen.getByLabelText('Problém:')).toBeInTheDocument();
  expect(screen.getByText('Nejčastější problémy')).toBeInTheDocument();
});

test('clicking a frequent problem filters the KB list', () => {
  setupMocks({ hasKb: true });
  render(<MarketingFeedbackPage />);
  fireEvent.click(screen.getByRole('button', { name: /akné/ }));
  expect(kbAdapter.useKbFeedbackAdapter).toHaveBeenLastCalledWith(
    expect.objectContaining({ problemTag: 'akné', pageNumber: 1 }),
  );
});

test('tag filters are not shown on other tabs', () => {
  setupMocks({ hasKb: true, hasLeaflet: true });
  render(<MarketingFeedbackPage />);
  fireEvent.click(screen.getByRole('button', { name: /letá/i }));
  expect(screen.queryByLabelText('Problém:')).not.toBeInTheDocument();
});
```

Adjust the leaflet tab-name matcher to match the existing `TAB_LABELS` text, and use `exact: true` if the short label also appears inside a longer aria-label (Playwright/RTL name matching is a substring match).

- [ ] **Step 8: Wire the page**

In `MarketingFeedbackPage.tsx`:

1. Imports:

```tsx
import KbQuestionTagFilters from '../components/feedback/KbQuestionTagFilters';
import KbTagFrequencyPanel from '../components/feedback/KbTagFrequencyPanel';
import { useKnowledgeBaseTagStatsQuery } from '../api/hooks/useKnowledgeBase';
import type { KbFeedbackParams } from '../components/feedback/types';
```

2. Type the KB state with the extended params:

```tsx
  const [kbParams, setKbParams] = useState<KbFeedbackParams>(DEFAULT_FEEDBACK_PARAMS);
```

3. After the adapter hooks:

```tsx
  const kbTagStats = useKnowledgeBaseTagStatsQuery({ dateFrom: kbParams.dateFrom, dateTo: kbParams.dateTo });

  const handleKbFilterChange = (update: Partial<KbFeedbackParams>) => {
    setKbParams((prev) => ({ ...prev, ...update, pageNumber: 1 }));
    setSelectedRowId(null);
  };
```

4. Directly after the `<GenericFeedbackFilters … />` element:

```tsx
        {activeTab === 'kb' && (
          <>
            <KbQuestionTagFilters
              dateFrom={kbParams.dateFrom}
              dateTo={kbParams.dateTo}
              problemTag={kbParams.problemTag}
              productTag={kbParams.productTag}
              problemOptions={kbTagStats.data?.problemTags ?? []}
              productOptions={kbTagStats.data?.productTags ?? []}
              onChange={handleKbFilterChange}
            />
            <KbTagFrequencyPanel
              stats={kbTagStats.data}
              isLoading={kbTagStats.isLoading}
              onProblemClick={(tag) => handleKbFilterChange({ problemTag: tag })}
              onProductClick={(tag) => handleKbFilterChange({ productTag: tag })}
            />
          </>
        )}
```

5. Row chips: in `GenericFeedbackTable.tsx`, render `<TagChips tags={row.tags ?? []} />` under the primary text cell, importing `TagChips from './TagChips'`. Detail modal: in `RagFeedbackDetailExtra.tsx`, add a section above the retrieved chunks:

```tsx
      {(log.problemTags.length > 0 || log.productTags.length > 0 || log.recommendedProductCodes.length > 0) && (
        <div className="space-y-1">
          <div className="text-xs font-semibold uppercase text-gray-500 dark:text-graphite-muted">Štítky</div>
          <TagChips tags={[...log.problemTags, ...log.productTags]} />
          {log.recommendedProductCodes.length > 0 && (
            <div className="text-xs text-gray-500 dark:text-graphite-muted">
              Doporučené produkty: {log.recommendedProductCodes.join(', ')}
            </div>
          )}
        </div>
      )}
```

Read `RagFeedbackDetailExtra.tsx` first and use its existing prop name for the log (the adapter passes `{ log }`).

- [ ] **Step 9: Run the feedback test suite**

```bash
cd frontend && CI=true npx react-scripts test --watchAll=false feedback MarketingFeedbackPage; cd -
```
Expected: PASS, including all pre-existing `GenericFeedbackTable` / `GenericFeedbackDetailModal` tests (chips render nothing when `tags` is absent).

- [ ] **Step 10: Commit**

```bash
git add frontend/src
git commit -m "feat: KB question tag filters, frequency panel and tag chips"
```

---

### Task 11: Full validation

**Files:** none new. Fix whatever the gates surface, in the files that caused it.

- [ ] **Step 1: Backend build, format and full unit suite**

```bash
dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet format Anela.Heblo.sln --verify-no-changes || dotnet format Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
```
Expected: build succeeds, format clean, 0 failed. An `AccessMatrixGen` crash in the build output is known non-fatal noise. If a failure's stack trace contradicts the source, the binaries are stale: touch the test project and rebuild.

- [ ] **Step 2: Integration suite (local only, CI skips it)**

```bash
podman machine start 2>/dev/null
dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "Category=Integration&(FullyQualifiedName~RagInteractionLogTagsIntegrationTests|FullyQualifiedName~KnowledgeBaseRepositoryIntegrationTests)"
```
Expected: 0 failed.

- [ ] **Step 3: Frontend build, lint and tests**

```bash
cd frontend && CI=false npm run build && npm run lint && CI=true npx react-scripts test --watchAll=false; cd -
```
Expected: build succeeds, no new lint errors (the existing `prefer-screen-queries` errors in `test/e2e` aren't from this change; check them against the pre-change file before taking ownership), and all tests pass.

- [ ] **Step 4: Confirm the migration is the only schema change, then commit**

```bash
git status --short
git diff --stat main...HEAD -- backend/src/Anela.Heblo.Persistence/Migrations
```
Expected: one new migration (+ Designer) and the snapshot.

```bash
git add -A backend frontend
git commit -m "chore: format and validation fixes for KB question tags" || echo "nothing to commit"
```

- [ ] **Step 5: Post-deploy manual check (record the result in the PR, not here)**

After the migration is applied manually to staging (`Heblo_TST`) and the branch is deployed:
1. On Poradenství (KB), ask 3–5 real questions from the spec's sample (akné for an 11-year-old, růžovka + vzorky, oteklé nohy + hořčíkové máslo, a "proč nejde objednat" operational question).
2. In Feedback → Poradenství (KB), confirm: problem tags appear; product tags only where a product was named; no problem tag on the operational question; recommended codes where the answer linked products.
3. Ask one question again and confirm the top retrieved sources match an earlier log row for the same question (retrieval unchanged).
4. Ask a question that fits an existing tag and confirm the model reused it rather than inventing a variant.

---

## Next step (not in this plan)

Backfill (spec §"Out of scope" #1): a one-off Hangfire job. For each KB row with empty tags, call `IQuestionTagLibrary.BuildExpansionConfigAsync` + `IRagQueryExpander.ExpandAsync` on the raw `Question` and persist **only** the tag columns, never `ExpandedQuery`. About 775 rows. Use `[AutomaticRetry(Attempts = 0)]`, process in batches, and make it resumable (skip rows that already have tags), because there's a single Hangfire worker.
