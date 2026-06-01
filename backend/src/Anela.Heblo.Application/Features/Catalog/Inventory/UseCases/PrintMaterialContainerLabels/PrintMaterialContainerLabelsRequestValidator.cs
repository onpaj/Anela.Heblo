using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsRequestValidator : AbstractValidator<PrintMaterialContainerLabelsRequest>
{
    public PrintMaterialContainerLabelsRequestValidator()
    {
        RuleFor(x => x.Count).GreaterThan(0).LessThanOrEqualTo(200)
            .WithMessage("Count must be between 1 and 200.");
    }
}
