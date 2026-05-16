using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

/// <summary>
/// No-op implementation of IGraphTodoService used when mock authentication is active
/// or BypassJwtValidation is set. Returns null/failure results so the application starts
/// cleanly without Azure AD token acquisition.
/// </summary>
public sealed class NoOpGraphTodoService : IGraphTodoService
{
    private readonly ILogger<NoOpGraphTodoService> _logger;

    public NoOpGraphTodoService(ILogger<NoOpGraphTodoService> logger)
    {
        _logger = logger;
    }

    public Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        _logger.LogWarning("Graph Todo disabled (mock auth active) — skipping ResolveUserIdByEmail for '{Email}'", email);
        return Task.FromResult<string?>(null);
    }

    public Task<TodoTaskResult> CreateTodoTaskAsync(
        string userId,
        string title,
        string description,
        DateTime? dueDate,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Graph Todo disabled (mock auth active) — skipping CreateTodoTask for user {UserId}", userId);
        return Task.FromResult(new TodoTaskResult(false, null, "Graph Todo disabled in mock auth mode."));
    }
}
