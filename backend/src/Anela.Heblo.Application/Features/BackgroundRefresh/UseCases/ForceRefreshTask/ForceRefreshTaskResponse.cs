namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.ForceRefreshTask;

public class ForceRefreshTaskResponse
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public string? ErrorMessage { get; init; }
}
