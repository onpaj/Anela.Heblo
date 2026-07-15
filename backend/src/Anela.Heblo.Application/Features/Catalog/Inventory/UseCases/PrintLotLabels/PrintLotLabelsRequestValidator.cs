using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotLabels;

public class PrintLotLabelsRequestValidator : AbstractValidator<PrintLotLabelsRequest>
{
    public PrintLotLabelsRequestValidator()
    {
        RuleFor(x => x.LotNumber)
            .NotEmpty().WithMessage("Lot number is required.")
            .MaximumLength(8).WithMessage("Lot number must be at most 8 characters.");

        RuleFor(x => x.Expiration)
            .NotEmpty().WithMessage("Expiration is required.")
            .Matches(@"^\d{2}/\d{2}$").WithMessage("Expiration must be in MM/YY format.");

        RuleFor(x => x.Count)
            .GreaterThan(0).LessThanOrEqualTo(200)
            .WithMessage("Count must be between 1 and 200.");
    }
}
