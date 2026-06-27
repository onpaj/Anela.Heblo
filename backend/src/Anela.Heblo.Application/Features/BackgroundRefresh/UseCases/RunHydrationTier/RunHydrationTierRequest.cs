using MediatR;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierRequest : IRequest<RunHydrationTierResponse>
{
    public int Tier { get; init; }
}
