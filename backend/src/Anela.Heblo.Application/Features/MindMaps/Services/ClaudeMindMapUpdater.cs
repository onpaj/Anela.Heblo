using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Anela.Heblo.Application.Features.MindMaps.Model;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MindMaps.Services;

public class ClaudeMindMapUpdater : IMindMapUpdater
{
    private const int MaxAttempts = 2;
    private const string PromptResourceName =
        "Anela.Heblo.Application.Features.MindMaps.Prompts.mindmap-update-skill.md";

    private static readonly Lazy<string> SystemPrompt = new(LoadSystemPrompt);

    // The map JSON is sent as plain prompt text to Claude. The default encoder escapes
    // all non-ASCII characters (Czech diacritics become \uXXXX); allowing the full
    // Unicode range keeps them readable while still escaping HTML/JS-sensitive
    // characters such as angle brackets and ampersands — unlike UnsafeRelaxedJsonEscaping.
    private static readonly JsonSerializerOptions UserMessageJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly IChatClient _chatClient;
    private readonly MindMapsOptions _options;
    private readonly ILogger<ClaudeMindMapUpdater> _logger;

    public ClaudeMindMapUpdater(
        IChatClient chatClient,
        IOptions<MindMapsOptions> options,
        ILogger<ClaudeMindMapUpdater> logger)
    {
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MindMapDocument> UpdateAsync(
        MindMapDocument current, MeetingTranscript meeting, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt.Value),
            new(ChatRole.User, BuildUserMessage(current, meeting))
        };
        var chatOptions = new ChatOptions { MaxOutputTokens = _options.UpdaterMaxOutputTokens };

        string? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
            var text = StripMarkdownCodeFence(response.Text ?? string.Empty);

            try
            {
                var doc = MindMapJson.Deserialize(text);
                var errors = MindMapDocumentValidator.Validate(doc);
                if (errors.Count == 0) return doc;
                lastError = string.Join(" ", errors);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any unexpected failure here (deserialization or otherwise) is treated as
                // "malformed reply" and retried, same as a validator error — never surfaced
                // as a raw exception the caller (IMindMapUpdater's contract) doesn't expect.
                lastError = ex.Message;
            }

            _logger.LogWarning(
                "Mind map update attempt {Attempt}/{Max} returned an invalid document: {Error}",
                attempt, MaxAttempts, lastError);
            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text ?? string.Empty));
            messages.Add(new ChatMessage(ChatRole.User,
                $"Your previous reply was not valid ({lastError}). " +
                "Return ONLY a valid JSON map document matching the given schema."));
        }

        throw new MindMapUpdateException(
            $"LLM returned an invalid mind map document after {MaxAttempts} attempts: {lastError}");
    }

    private static string BuildUserMessage(MindMapDocument current, MeetingTranscript meeting)
    {
        // The LLM view deliberately omits position/collapsed/lockedBy — those are
        // UI/system metadata the guard pass restores after the update.
        var llmView = new
        {
            rootNodeId = current.RootNodeId,
            nodes = current.Nodes.Select(n => new
            {
                id = n.Id,
                parentId = n.ParentId,
                title = n.Title,
                notes = n.Notes,
                status = n.Status,
                owner = n.Owner,
                locked = n.LockedBy != null,
                sourceMeetingIds = n.SourceMeetingIds
            }),
            doNotRecreate = current.SuppressedNodes.Select(s => s.Title)
        };
        var mapJson = JsonSerializer.Serialize(llmView, UserMessageJsonOptions);

        // Participants bound the set of names the prompt allows in `owner`. Omitted entirely
        // when the meeting has none, so the model never sees an empty roster it could read
        // as "nobody was there".
        var participants = meeting.Participants.Count > 0
            ? $"Participants: {string.Join(", ", meeting.Participants)}\n\n"
            : string.Empty;

        return $"Current map:\n{mapJson}\n\n" +
               $"New meeting — {meeting.Subject} ({meeting.PlaudCreatedAt:yyyy-MM-dd}):\n\n" +
               participants +
               $"Summary:\n{meeting.Summary}\n\n" +
               $"Transcript:\n{meeting.RawTranscript}";
    }

    private static string LoadSystemPrompt()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{PromptResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string StripMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["```json".Length..];
        else if (trimmed.StartsWith("```"))
            trimmed = trimmed["```".Length..];
        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^"```".Length];
        return trimmed.Trim();
    }
}
