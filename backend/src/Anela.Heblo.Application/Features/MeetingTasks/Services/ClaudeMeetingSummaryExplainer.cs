using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class ClaudeMeetingSummaryExplainer : IMeetingSummaryExplainer
{
    private const string SystemPrompt = """
        Jsi asistent, který vysvětluje, proč se daná část shrnutí schůzky dostala do souhrnu.
        Dostaneš celý přepis schůzky a vybraný text (fragment shrnutí nebo navrhované úlohy).
        Proveď toto:
        1. Cituj přesný úsek přepisu, který vedl k tomuto bodu.
        2. Napiš podrobné vysvětlení v češtině, proč tento úsek skončil ve shrnutí nebo jako úloha.
        Odpověz POUZE jako JSON (bez dalšího textu):
        { "relevantTranscript": "...", "explanation": "..." }
        """;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IChatClient _chatClient;
    private readonly ILogger<ClaudeMeetingSummaryExplainer> _logger;

    public ClaudeMeetingSummaryExplainer(
        IChatClient chatClient,
        ILogger<ClaudeMeetingSummaryExplainer> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<MeetingSummaryExplanation> ExplainAsync(
        string transcript,
        string selectedText,
        CancellationToken ct = default)
    {
        try
        {
            var messages = new[]
            {
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User,
                    $"Vybraný text: {selectedText}\n\nCelý přepis:\n{transcript}")
            };

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var text = StripMarkdownCodeFence(response.Text ?? string.Empty);

            var result = JsonSerializer.Deserialize<MeetingSummaryExplanation>(
                text,
                _jsonOptions);

            return result ?? FallbackExplanation();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get explanation from Claude");
            return FallbackExplanation();
        }
    }

    private static MeetingSummaryExplanation FallbackExplanation() =>
        new() { RelevantTranscript = string.Empty, Explanation = "Vysvětlení není k dispozici." };

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
