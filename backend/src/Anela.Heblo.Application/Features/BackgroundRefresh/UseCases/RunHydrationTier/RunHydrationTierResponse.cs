using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierResponse : BaseResponse
{
    public bool NotFound { get; set; }
    public bool Cancelled { get; set; }
    public string? ErrorMessage { get; set; }
    public int TaskCount { get; set; }
}
