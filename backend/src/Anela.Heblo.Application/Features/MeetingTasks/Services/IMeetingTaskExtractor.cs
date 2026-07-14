namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record ExtractedTask(
    string Title,
    string Description,
    string Assignee,
    DateTime? DueDate,
    string? AssigneeEmail = null);

public record MeetingExtractionResult(List<ExtractedTask> Tasks, List<string> Participants);

public interface IMeetingTaskExtractor
{
    Task<MeetingExtractionResult> ExtractAsync(string summary, string transcript, CancellationToken ct = default);
}
