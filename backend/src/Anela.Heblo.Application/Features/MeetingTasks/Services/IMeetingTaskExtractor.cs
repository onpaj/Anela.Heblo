namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record ExtractedTask(
    string Title,
    string Description,
    string Assignee,
    DateTime? DueDate,
    string? AssigneeEmail = null);

public interface IMeetingTaskExtractor
{
    Task<List<ExtractedTask>> ExtractAsync(string summary, string transcript, CancellationToken ct = default);
}
