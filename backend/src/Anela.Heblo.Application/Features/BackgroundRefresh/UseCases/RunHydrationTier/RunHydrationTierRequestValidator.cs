using FluentValidation;

namespace Anela.Heblo.Application.Features.BackgroundRefresh.UseCases.RunHydrationTier;

public class RunHydrationTierRequestValidator : AbstractValidator<RunHydrationTierRequest>
{
    public RunHydrationTierRequestValidator()
    {
        RuleFor(x => x.Tier).GreaterThan(0);
    }
}
