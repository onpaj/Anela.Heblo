namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierResponse
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public bool Cancelled { get; init; }
    public string? ErrorMessage { get; init; }
    public int TaskCount { get; init; }
}
