using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// No-op implementation of IMeetingTaskExporter used when mock authentication is active
/// or BypassJwtValidation is set. Returns null/failure results so the application starts
/// cleanly without Azure AD token acquisition.
/// </summary>
public sealed class NoOpMeetingTaskExporter : IMeetingTaskExporter
{
    private readonly ILogger<NoOpMeetingTaskExporter> _logger;

    public NoOpMeetingTaskExporter(ILogger<NoOpMeetingTaskExporter> logger)
    {
        _logger = logger;
    }

    public Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        _logger.LogWarning("Planner export disabled (mock auth active) — skipping ResolveUserIdByEmail for '{Email}'", email);
        return Task.FromResult<string?>(null);
    }

    public Task<MeetingTaskExportResult> ExportTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Planner export disabled (mock auth active) — skipping ExportTask for user {UserId}", userId);
        return Task.FromResult(new MeetingTaskExportResult(false, null, "Planner export disabled in mock auth mode."));
    }
}
