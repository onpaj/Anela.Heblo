namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

public record TodoTaskResult(bool Success, string? ExternalTaskId, string? Error);

public interface IGraphTodoService
{
    Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default);

    Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default);
}
