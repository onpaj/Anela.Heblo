using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersRequestValidator : AbstractValidator<CreateMaterialContainersRequest>
{
    public CreateMaterialContainersRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one item is required.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(500).WithMessage("Cannot create more than 500 containers in one call.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Code)
                .NotEmpty()
                .Matches(@"^M\d{8}$")
                .WithMessage("Code must match format M followed by 8 digits (e.g. M00000001).");
            item.RuleFor(i => i.MaterialCode).NotEmpty().MaximumLength(InventoryConstants.MaterialCodeMaxLength);
            item.RuleFor(i => i.LotCode).NotEmpty().MaximumLength(InventoryConstants.LotCodeMaxLength);
            item.RuleFor(i => i.Amount).GreaterThan(0).When(i => i.Amount.HasValue);
            item.RuleFor(i => i.Unit).NotEmpty().MaximumLength(InventoryConstants.UnitMaxLength).When(i => i.Unit != null);
        });
    }
}
