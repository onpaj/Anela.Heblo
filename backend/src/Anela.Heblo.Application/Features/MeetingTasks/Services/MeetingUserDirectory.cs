using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// Loads the static user directory from a JSON file once at construction.
/// Registered as a singleton — the file is read a single time per process.
/// A missing or malformed file degrades gracefully to an empty directory.
/// </summary>
public sealed class MeetingUserDirectory : IMeetingUserDirectory
{
    private readonly IReadOnlyList<MeetingUser> _users;

    public MeetingUserDirectory(IOptions<MeetingTasksOptions> options, ILogger<MeetingUserDirectory> logger)
    {
        _users = Load(options.Value.UserDirectoryPath, logger);
    }

    public IReadOnlyList<MeetingUser> GetAll() => _users;

    public MeetingUser? Resolve(string nameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(nameOrAlias))
            return null;

        var direct = FindUser(nameOrAlias);
        if (direct is not null)
            return direct;

        var parts = nameOrAlias.Split(['&', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
            return null;

        return parts.Select(FindUser).FirstOrDefault(u => u is not null);
    }

    private MeetingUser? FindUser(string name) =>
        _users.FirstOrDefault(u =>
            string.Equals(u.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
            u.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));

    private static IReadOnlyList<MeetingUser> Load(string path, ILogger logger)
    {
        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);

        if (!File.Exists(fullPath))
        {
            logger.LogError("Meeting user directory file not found at {Path}; using empty directory", fullPath);
            return Array.Empty<MeetingUser>();
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var entries = JsonSerializer.Deserialize<List<DirectoryEntry>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries is null)
                return Array.Empty<MeetingUser>();

            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Email))
                .Select(e => new MeetingUser(
                    e.Email,
                    e.DisplayName ?? string.Empty,
                    e.Aliases ?? new List<string>()))
                .ToList();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse meeting user directory at {Path}; using empty directory", fullPath);
            return Array.Empty<MeetingUser>();
        }
    }

    private sealed class DirectoryEntry
    {
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("aliases")] public List<string>? Aliases { get; set; }
    }
}
