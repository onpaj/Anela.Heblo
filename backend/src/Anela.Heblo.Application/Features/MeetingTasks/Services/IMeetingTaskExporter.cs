namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record MeetingTaskExportResult(bool Success, string? ExternalTaskId, string? Error);

public interface IMeetingTaskExporter
{
    Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default);

    Task<MeetingTaskExportResult> ExportTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default);
}
