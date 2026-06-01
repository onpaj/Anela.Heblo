using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;

public class CreateEansRequestValidator : AbstractValidator<CreateEansRequest>
{
    public CreateEansRequestValidator()
    {
        RuleFor(x => x.LotId).GreaterThan(0).WithMessage("LotId is required.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(500).WithMessage("Cannot create more than 500 EANs in one call.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Amount).GreaterThan(0).WithMessage("Amount must be positive.");
            item.RuleFor(i => i.Unit).NotEmpty().MaximumLength(InventoryConstants.UnitMaxLength);
        });
    }
}
