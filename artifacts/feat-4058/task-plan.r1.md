# Harden ClaudeMeetingTaskExtractor Against Malformed JSON Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `ClaudeMeetingTaskExtractor.ExtractAsync` from silently dropping all meeting tasks when Claude returns non-JSON output — add a bounded retry with a JSON-recovery fallback, and raise a distinct, loud failure only when every attempt is exhausted.

**Architecture:** `ExtractAsync` gains a bounded (3-attempt) retry loop around "call chat client → strip markdown fence → parse → fall back to bracket-matched JSON extraction → validate task titles"; exhausting all attempts logs the raw response and throws a new `MeetingTaskExtractionFailedException` instead of returning an empty-but-"successful" result. The two existing callers (`IngestPlaudRecordingHandler`, `ReimportMeetingTranscriptHandler`) catch that exception and turn it into their own existing failure-response shape instead of persisting a transcript reported as successful with silently-empty tasks. `PlaudPollingJob`'s per-recording summary counters are updated so a caught extraction failure is not miscounted as "ingested".

**Tech Stack:** .NET 8, `System.Text.Json`, `Microsoft.Extensions.AI` (`IChatClient`), MediatR, xUnit + Moq + FluentAssertions (existing test stack, no new packages).

---

### task: extraction-failure-exception

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTaskExtractionFailedExceptionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MeetingTasks;

public sealed class MeetingTaskExtractionFailedExceptionTests
{
    [Fact]
    public void Constructor_SetsMessageAttemptCountAndLastRawResponse()
    {
        var ex = new MeetingTaskExtractionFailedException("boom", 3, "not-json{{{");

        ex.Message.Should().Be("boom");
        ex.AttemptCount.Should().Be(3);
        ex.LastRawResponse.Should().Be("not-json{{{");
    }

    [Fact]
    public void Constructor_AllowsNullLastRawResponse()
    {
        var ex = new MeetingTaskExtractionFailedException("boom", 3, null);

        ex.LastRawResponse.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTaskExtractionFailedExceptionTests"`
Expected: FAIL to compile — `MeetingTaskExtractionFailedException` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Thrown by <see cref="IMeetingTaskExtractor.ExtractAsync"/> when the LLM's response
/// could not be parsed into a valid, schema-conforming payload after exhausting all
/// retry attempts. Callers must not treat this as "zero tasks found" — it signals the
/// extraction itself failed and no tasks could be recovered.
/// </summary>
public sealed class MeetingTaskExtractionFailedException : Exception
{
    /// <summary>Total number of attempts made (initial call + retries) before giving up.</summary>
    public int AttemptCount { get; }

    /// <summary>The raw (fence-stripped) response text from the final failed attempt, for diagnostics.</summary>
    public string? LastRawResponse { get; }

    public MeetingTaskExtractionFailedException(string message, int attemptCount, string? lastRawResponse)
        : base(message)
    {
        AttemptCount = attemptCount;
        LastRawResponse = lastRawResponse;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MeetingTaskExtractionFailedExceptionTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs backend/test/Anela.Heblo.Tests/Features/MeetingTasks/MeetingTaskExtractionFailedExceptionTests.cs
git commit -m "feat(meeting-tasks): add MeetingTaskExtractionFailedException"
```

---

### task: extractor-retry-and-recovery

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs`

This task depends on `extraction-failure-exception` (uses `MeetingTaskExtractionFailedException`).

- [ ] **Step 1: Write the failing tests**

Add these tests to `ClaudeMeetingTaskExtractorTests.cs` (keep existing tests in the file — they will be adjusted in Step 1b below, not deleted):

```csharp
    [Fact]
    public async Task ExtractAsync_WhenFirstAttemptMalformed_RetriesAndReturnsSecondAttemptResult()
    {
        var callCount = 0;
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                var text = callCount == 1
                    ? "not-valid-json{{{"
                    : """{"participants":["Bob"],"tasks":[{"title":"Action","description":"Do it","assignee":"Bob","assigneeEmail":null,"dueDate":null}]}""";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Tasks.Should().HaveCount(1);
        result.Tasks[0].Title.Should().Be("Action");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ExtractAsync_WhenAllAttemptsMalformed_ThrowsAfterExhaustingRetries()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "not-valid-json{{{")]));

        var act = async () => await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<MeetingTaskExtractionFailedException>();
        ex.Which.AttemptCount.Should().Be(3);
        ex.Which.LastRawResponse.Should().Be("not-valid-json{{{");

        _mockChatClient.Verify(x => x.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task ExtractAsync_WithJsonEmbeddedInProseText_ExtractsAndParsesIt()
    {
        SetupResponse("Here is the extracted result:\n" +
            """{"participants":["Bob"],"tasks":[{"title":"Action","description":"Do it","assignee":"Bob","assigneeEmail":null,"dueDate":null}]}""" +
            "\nLet me know if you need anything else.");

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Tasks.Should().HaveCount(1);
        result.Tasks[0].Title.Should().Be("Action");
    }

    [Fact]
    public async Task ExtractAsync_WhenTaskHasEmptyTitle_RetriesAndSucceedsOnNextAttempt()
    {
        var callCount = 0;
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                var text = callCount == 1
                    ? """{"participants":[],"tasks":[{"title":"","description":"D","assignee":"","assigneeEmail":null,"dueDate":null}]}"""
                    : """{"participants":[],"tasks":[{"title":"Real title","description":"D","assignee":"","assigneeEmail":null,"dueDate":null}]}""";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Tasks.Should().ContainSingle(t => t.Title == "Real title");
    }

    [Fact]
    public async Task ExtractAsync_WhenChatClientThrowsOnFirstAttemptButSucceedsOnRetry_ReturnsResult()
    {
        var callCount = 0;
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("transient");
                return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, EmptyPayload)]));
            });

        var result = await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);

        result.Tasks.Should().BeEmpty();
        callCount.Should().Be(2);
    }
```

- [ ] **Step 1b: Update the two existing tests whose expected behavior changes**

Replace `ExtractAsync_WhenJsonInvalid_LogsErrorAndReturnsEmpty` (which previously asserted a single malformed response immediately returns an empty result) — that behavior is now `ExtractAsync_WhenAllAttemptsMalformed_ThrowsAfterExhaustingRetries` above. Delete the old test body and replace it with:

```csharp
    [Fact]
    public async Task ExtractAsync_WhenAllAttemptsMalformed_LogsFinalErrorWithRawResponseAndAttemptCount()
    {
        _mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "not-valid-json{{{")]));

        var act = async () => await _extractor.ExtractAsync("summary", "transcript", CancellationToken.None);
        await act.Should().ThrowAsync<MeetingTaskExtractionFailedException>();

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("not-valid-json{{{")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

`ExtractAsync_WhenApiThrows_LogsErrorAndReturnsEmpty` / `ExtractAsync_WhenChatClientThrows_ReturnsEmptyResult` (transport failure on every attempt) keep asserting an empty result, per the design's documented scope boundary (transport failure on the *final* attempt still falls through to the existing "log + empty result" path, only content/parse failures throw the new exception) — but the chat client mock must now return the `HttpRequestException` on **every** call (not just once), since the extractor will retry a transient transport failure. Update both to use `.ThrowsAsync(new HttpRequestException("API error"))` as they already do (this already throws on every call by default with Moq's `.ThrowsAsync`, so no change needed there) and add an assertion that `GetResponseAsync` was called 3 times (`Times.Exactly(3)`), to lock in that transport retries happen before falling back to the empty-result path.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"`
Expected: FAIL — new tests fail because retry/recovery logic doesn't exist yet; `ExtractAsync_WhenAllAttemptsMalformed_*` tests fail because the current code returns an empty result instead of throwing.

- [ ] **Step 3: Write minimal implementation**

Replace the body of `ClaudeMeetingTaskExtractor.cs` from the `MaxOutputTokens` constant declaration through the end of `ExtractAsync`, and add the new private helpers, so the full file reads:

```csharp
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class ClaudeMeetingTaskExtractor : IMeetingTaskExtractor
{
    private const string BasePrompt = """
        Jsi asistent, který z transkriptu schůzky extrahuje účastníky a akční položky.
        Vrať POUZE JSON objekt (bez dalšího textu) s těmito poli:
        - participants: pole jmen všech osob, které se schůzky zúčastnily (odvoď z
          transkriptu; každé jméno uveď jen jednou, bez duplicit)
        - tasks: pole akčních položek, kde každá položka má tato pole:
          - title: stručný název úkolu
          - description: podrobný popis úkolu
          - assignee: jméno osoby odpovědné za splnění (nebo prázdný řetězec)
          - assigneeEmail: e-mail osoby ze seznamu známých uživatelů níže, pokud
            jméno nebo přezdívku v transkriptu dokážeš spolehlivě přiřadit ke
            konkrétnímu uživateli; jinak null
          - dueDate: datum splnění ve formátu ISO 8601 (nebo null)
        """;

    private const string NoUsersNote =
        "\n\nSeznam známých uživatelů je prázdný — assigneeEmail vždy nastav na null.";

    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;
    private readonly IMeetingUserDirectory _userDirectory;
    private readonly ILogger<ClaudeMeetingTaskExtractor> _logger;

    public ClaudeMeetingTaskExtractor(
        IChatClient chatClient,
        IMeetingUserDirectory userDirectory,
        ILogger<ClaudeMeetingTaskExtractor> logger)
    {
        _chatClient = chatClient;
        _userDirectory = userDirectory;
        _logger = logger;
    }

    public async Task<MeetingExtractionResult> ExtractAsync(
        string summary,
        string transcript,
        CancellationToken ct = default)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt()),
            new ChatMessage(ChatRole.User, $"Souhrn: {summary}\n\nTranskript: {transcript}")
        };

        var chatOptions = new ChatOptions { MaxOutputTokens = 8192 };

        string? lastRawResponse = null;
        string lastFailureReason = "unknown";

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string text;
            try
            {
                var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
                text = StripMarkdownCodeFence(response.Text ?? string.Empty);
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                lastFailureReason = ex.Message;
                _logger.LogWarning(ex,
                    "Meeting task extraction attempt {Attempt}/{MaxAttempts} failed with a transport error, retrying",
                    attempt, MaxAttempts);
                continue;
            }
            catch (Exception ex)
            {
                // Final attempt's transport failure: preserve the pre-existing
                // "log + empty result" contract rather than the new throwing path —
                // this issue's fingerprint is a content/parse failure, not transport.
                _logger.LogError(ex, "Meeting task extraction failed — transcript will be imported without tasks");
                return new MeetingExtractionResult([], []);
            }

            lastRawResponse = text;

            if (TryParseAndValidate(text, out var result, out var failureReason))
                return result;

            lastFailureReason = failureReason!;

            if (attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    "Meeting task extraction attempt {Attempt}/{MaxAttempts} produced an invalid response ({Reason}), retrying",
                    attempt, MaxAttempts, failureReason);
            }
        }

        _logger.LogError(
            "Meeting task extraction failed after {MaxAttempts} attempts ({Reason}) — raw response: {RawResponse}",
            MaxAttempts, lastFailureReason, lastRawResponse);
        throw new MeetingTaskExtractionFailedException(
            $"Meeting task extraction failed after {MaxAttempts} attempts: {lastFailureReason}",
            MaxAttempts,
            lastRawResponse);
    }

    private bool TryParseAndValidate(string text, out MeetingExtractionResult result, out string? failureReason)
    {
        result = null!;
        failureReason = null;

        if (!TryDeserialize(text, out var payload))
        {
            var embedded = ExtractEmbeddedJsonObject(text);
            if (embedded is null || !TryDeserialize(embedded, out payload))
            {
                failureReason = "malformed JSON";
                return false;
            }
        }

        if (payload!.Tasks?.Any(t => string.IsNullOrWhiteSpace(t.Title)) == true)
        {
            failureReason = "task with empty title";
            return false;
        }

        var tasks = payload.Tasks ?? [];
        var participants = NormalizeParticipants(payload.Participants);

        if (tasks.Count == 0)
        {
            _logger.LogWarning("Meeting task extraction completed with no tasks — Claude returned an empty array");
        }

        result = new MeetingExtractionResult(tasks, participants);
        return true;
    }

    private static bool TryDeserialize(string text, out ExtractionPayload? payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<ExtractionPayload>(text, JsonOptions);
            return payload is not null;
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
    }

    /// <summary>
    /// Scans for the first top-level '{' and its matching closing '}', tracking
    /// string/escape state so braces inside quoted string values (e.g. a task
    /// description) don't throw off the match. Returns null if no balanced
    /// top-level object is found.
    /// </summary>
    private static string? ExtractEmbeddedJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escapeNext = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (escapeNext)
            {
                escapeNext = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escapeNext = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
                continue;

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }

        return null;
    }

    private static List<string> NormalizeParticipants(List<string>? participants)
    {
        if (participants is null || participants.Count == 0)
            return [];

        return participants
            .Select(p => p?.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record ExtractionPayload(List<string>? Participants, List<ExtractedTask>? Tasks);

    private string BuildSystemPrompt()
    {
        var users = _userDirectory.GetAll();
        if (users.Count == 0)
            return BasePrompt + NoUsersNote;

        var sb = new StringBuilder(BasePrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Seznam známých uživatelů (assigneeEmail vybírej pouze z tohoto seznamu):");
        foreach (var user in users)
        {
            var aliases = user.Aliases.Count > 0 ? $" (přezdívky: {string.Join(", ", user.Aliases)})" : string.Empty;
            sb.AppendLine($"- {user.DisplayName}{aliases} → {user.Email}");
        }
        return sb.ToString();
    }

    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```json"))
        {
            trimmed = trimmed["```json".Length..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed["```".Length..];
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^"```".Length];
        }

        return trimmed.Trim();
    }
}
```

Note the `catch (Exception ex) when (attempt < MaxAttempts)` / final `catch (Exception ex)` pair: this deliberately keeps a transient transport failure on the *last* attempt following the pre-existing "log + empty result" contract (out of scope for this issue per the architecture review), while still retrying transport failures on earlier attempts so a single blip doesn't need to consume the JSON-repair budget.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ClaudeMeetingTaskExtractorTests"`
Expected: PASS (all tests, old and new)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs backend/test/Anela.Heblo.Tests/Features/MeetingTasks/ClaudeMeetingTaskExtractorTests.cs
git commit -m "fix(meeting-tasks): retry and recover malformed Claude JSON responses instead of silently dropping tasks"
```

---

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

---

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

---

### task: full-verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full backend test suite**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes && dotnet test`
Expected: Build succeeds, `dotnet format` reports no changes needed, all tests pass (including the new and modified tests from every prior task in this plan).

- [ ] **Step 2: Manually re-read the diff against the spec**

Confirm each of spec.r1.md's FR-1 through FR-5 and NFR-1/NFR-2 is satisfied by the committed changes:
- FR-1/FR-2 (validate + unwrap): `TryParseAndValidate` + `ExtractEmbeddedJsonObject`.
- FR-3 (bounded retry): `MaxAttempts = 3` loop in `ExtractAsync`.
- FR-4 (loud, distinct failure): `MeetingTaskExtractionFailedException` thrown only after exhausting retries, with raw response + attempt count logged.
- FR-5 (no silent task loss): both callers now catch the exception and report failure instead of persisting an empty-but-"successful" transcript; `PlaudPollingJob` counts failures distinctly.
- NFR-1 (happy path unaffected): a valid first-attempt response makes exactly one chat-client call, same as before.
- NFR-2 (observability): every attempt is logged (warning on retry, error on final failure with raw response + attempt count).

- [ ] **Step 3: No commit for this task** — it is verification-only. If Step 1 or Step 2 surfaces a gap, fix it within the relevant prior task's files and amend that task's commit before proceeding (do not create a new "fixup" task).
