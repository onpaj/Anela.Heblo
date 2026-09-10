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
