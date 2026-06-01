namespace Anela.Heblo.Xcc.Services.BackgroundRefresh;

public class TaskRegistrationInfo
{
    public required string TaskId { get; init; }
    public required Func<IServiceProvider, CancellationToken, Task> RefreshMethod { get; init; }
    public RefreshTaskConfiguration? Configuration { get; init; }
}