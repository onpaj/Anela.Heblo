using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.FeedLotMedia;

public class FeedLotMediaRequestValidator : AbstractValidator<FeedLotMediaRequest>
{
    public FeedLotMediaRequestValidator()
    {
        RuleFor(x => x.Dots)
            .GreaterThan(0).LessThanOrEqualTo(200)
            .WithMessage("Feed distance must be between 1 and 200 dots.");
    }
}
