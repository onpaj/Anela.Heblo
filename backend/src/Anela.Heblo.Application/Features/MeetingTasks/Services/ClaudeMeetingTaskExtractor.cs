using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public sealed class ClaudeMeetingTaskExtractor : IMeetingTaskExtractor
{
    private const string BasePrompt = """
        Jsi asistent, který z transkriptu schůzky extrahuje akční položky.
        Z dodaného souhrnu a transkriptu schůzky extrahuj všechny akční položky.
        Vrať POUZE JSON pole (bez dalšího textu) obsahující objekty s těmito poli:
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

    public async Task<List<ExtractedTask>> ExtractAsync(
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

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, chatOptions, ct);
            var text = StripMarkdownCodeFence(response.Text ?? string.Empty);

            try
            {
                var result = JsonSerializer.Deserialize<List<ExtractedTask>>(text, JsonOptions) ?? [];

                if (result.Count == 0)
                {
                    _logger.LogWarning("Meeting task extraction completed with no tasks — Claude returned an empty array");
                }

                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Meeting task extraction returned malformed JSON — transcript will be imported without tasks");
                return [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Meeting task extraction failed — transcript will be imported without tasks");
            return [];
        }
    }

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
