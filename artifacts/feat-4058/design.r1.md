# Design: Harden ClaudeMeetingTaskExtractor against malformed LLM JSON responses

## Component Design

### `ClaudeMeetingTaskExtractor` (modified)
`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`

Responsibility stays the same — call the chat client, turn its response into `MeetingExtractionResult` — but `ExtractAsync` now wraps the "call + parse + validate" sequence in a bounded retry loop instead of a single attempt, and distinguishes "genuinely zero tasks" from "could not get a usable response."

```
private const int MaxAttempts = 3; // 1 initial + 2 retries

public async Task<MeetingExtractionResult> ExtractAsync(string summary, string transcript, CancellationToken ct = default)
{
    var messages = BuildMessages(summary, transcript);
    var chatOptions = new ChatOptions { MaxOutputTokens = 8192 };

    string? lastRawResponse = null;
    string? lastFailureReason = null;

    for (var attempt = 1; attempt <= MaxAttempts; attempt++)
    {
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
            var text = StripMarkdownCodeFence(response.Text ?? string.Empty);
            lastRawResponse = text;

            if (TryParseAndValidate(text, out var result, out var failureReason))
                return result;

            lastFailureReason = failureReason;
            _logger.LogWarning(
                "Meeting task extraction attempt {Attempt}/{MaxAttempts} produced an invalid response ({Reason}), retrying",
                attempt, MaxAttempts, failureReason);
        }
        catch (Exception ex) when (attempt < MaxAttempts)
        {
            // transport-level failure (e.g. HttpRequestException) on a non-final attempt:
            // log and retry rather than aborting immediately.
            lastFailureReason = ex.Message;
            _logger.LogWarning(ex,
                "Meeting task extraction attempt {Attempt}/{MaxAttempts} failed with a transport error, retrying",
                attempt, MaxAttempts);
        }
        // NOTE: transport failure on the FINAL attempt is deliberately NOT caught here —
        // it falls through to the existing outer behavior via rethrow, preserving today's
        // "transport failure -> log + empty result" contract for exhausted transport retries.
        // (See Data Schemas note below on why this is a conscious scope boundary.)
    }

    _logger.LogError(
        "Meeting task extraction failed after {MaxAttempts} attempts ({Reason}) — raw response: {RawResponse}",
        MaxAttempts, lastFailureReason, lastRawResponse);
    throw new MeetingTaskExtractionFailedException(
        $"Meeting task extraction failed after {MaxAttempts} attempts: {lastFailureReason}",
        MaxAttempts,
        lastRawResponse);
}
```

`TryParseAndValidate` centralizes what today is inline `try { Deserialize... } catch (JsonException)`:

```
private bool TryParseAndValidate(string text, out MeetingExtractionResult result, out string? failureReason)
{
    result = default!;
    failureReason = null;

    var candidate = text;
    if (!TryDeserialize(candidate, out var payload))
    {
        // fallback: response wasn't clean JSON even after fence-stripping —
        // try extracting an embedded {...} object from surrounding text.
        candidate = ExtractEmbeddedJsonObject(text);
        if (candidate is null || !TryDeserialize(candidate, out payload))
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
        _logger.LogWarning("Meeting task extraction completed with no tasks — Claude returned an empty array");

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

// Scans for the first '{' and its matching closing '}', tracking string/escape
// state so braces inside quoted string values don't throw off the match.
// Returns null if no balanced top-level object is found.
private static string? ExtractEmbeddedJsonObject(string text) { /* bracket-matching, see below */ }
```

`ExtractEmbeddedJsonObject` walks the text once, tracking `depth` (brace nesting) and `inString`/`escapeNext` flags so a `{` or `}` character inside a JSON string value (e.g. inside a task's `description`) is not mistaken for structural nesting. It returns the substring from the first top-level `{` to its matching `}`, or `null` if none is found — the caller treats `null` the same as a failed parse.

### `MeetingTaskExtractionFailedException` (new)
`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingTaskExtractionFailedException.cs`

```csharp
namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class MeetingTaskExtractionFailedException : Exception
{
    public int AttemptCount { get; }
    public string? LastRawResponse { get; }

    public MeetingTaskExtractionFailedException(string message, int attemptCount, string? lastRawResponse)
        : base(message)
    {
        AttemptCount = attemptCount;
        LastRawResponse = lastRawResponse;
    }
}
```

### `IngestPlaudRecordingHandler` (modified)
`backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`

Wraps the extractor call; on the new exception, logs and returns the handler's existing failure shape instead of proceeding to persist a transcript reported as `Success = true` with silently-empty tasks:

```csharp
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
(Exact field name(s) on `IngestPlaudRecordingResponse` used to signal this failure — reuse whatever the response type already exposes for a non-success outcome; do not add a new field if `Success = false` alone is sufficient for existing consumers of this response.)

### `ReimportMeetingTranscriptHandler` (modified)
`backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs`

Same pattern: wrap the `_extractor.ExtractAsync(...)` call, catch `MeetingTaskExtractionFailedException`, log with the transcript identifier this handler already has in scope, and return that handler's own existing failure-response shape (mirror its current convention — inspect the handler's other early-return paths for the pattern to follow, do not invent a new one).

## Data Schemas

No persisted schema changes. In-memory shapes only:

- `MeetingExtractionResult(List<ExtractedTask> Tasks, List<string> Participants)` — **unchanged** on success; it is no longer returned as an empty-but-successful result for the "could not parse after retries" case (that case now throws instead).
- `ExtractedTask(string Title, string Description, string Assignee, DateTime? DueDate, string? AssigneeEmail = null)` — unchanged. Validation only *rejects* a payload where any task has an empty `Title` (triggers a retry); it does not change the shape.
- `ExtractionPayload(List<string>? Participants, List<ExtractedTask>? Tasks)` (private record, deserialization target) — unchanged.
- New: `MeetingTaskExtractionFailedException` — not a data schema, but its two extra properties (`AttemptCount`, `LastRawResponse`) are the "payload" callers/log sinks get on final failure, replacing today's implicit "just look at the preceding LogError line" diagnostic path with structured, catchable data.

**Scope boundary on transport failures:** the design above only adds a *retry* for transport failures on non-final attempts (so a single transient network blip doesn't need to also exhaust the JSON-repair budget), but preserves the existing "log + return empty result" behavior when a transport failure occurs on the last attempt — per the architecture review, changing that path is out of scope for this issue (the telemetry fingerprint here is specifically `JsonReaderException`, a content problem, not a transport error, and `AnthropicChatClient` already retries transport failures below this layer via Polly). If a reviewer prefers transport failures to always propagate through the same `MeetingTaskExtractionFailedException` path for consistency, that is a one-line change to move the final attempt's `catch` clause inside the loop's `when` guard — flagged here for the planner/developer to confirm rather than silently deciding either way.
