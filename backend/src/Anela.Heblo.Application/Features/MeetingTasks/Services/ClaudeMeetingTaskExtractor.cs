using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class ClaudeMeetingTaskExtractor : IMeetingTaskExtractor
{
    private const string SystemPrompt = """
        Jsi asistent, který z transkriptu schůzky extrahuje akční položky.
        Z dodaného souhrnu a transkriptu schůzky extrahuj všechny akční položky.
        Vrať POUZE JSON pole (bez dalšího textu) obsahující objekty s těmito poli:
        - title: stručný název úkolu
        - description: podrobný popis úkolu
        - assignee: jméno osoby odpovědné za splnění (nebo prázdný řetězec)
        - dueDate: datum splnění ve formátu ISO 8601 (nebo null)
        """;

    private readonly IChatClient _chatClient;
    private readonly ILogger<ClaudeMeetingTaskExtractor> _logger;

    public ClaudeMeetingTaskExtractor(
        IChatClient chatClient,
        ILogger<ClaudeMeetingTaskExtractor> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<List<ExtractedTask>> ExtractAsync(
        string summary,
        string transcript,
        CancellationToken ct = default)
    {
        try
        {
            var messages = new[]
            {
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, $"Souhrn: {summary}\n\nTranskript: {transcript}")
            };

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var text = response.Text ?? string.Empty;

            // Strip Markdown code fence if present
            text = StripMarkdownCodeFence(text);

            var result = JsonSerializer.Deserialize<List<ExtractedTask>>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return result ?? new List<ExtractedTask>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to extract tasks via Claude — transcript will be imported without tasks");
            return new List<ExtractedTask>();
        }
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
