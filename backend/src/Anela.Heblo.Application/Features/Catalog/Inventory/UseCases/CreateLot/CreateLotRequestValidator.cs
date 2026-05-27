using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotRequestValidator : AbstractValidator<CreateLotRequest>
{
    public CreateLotRequestValidator()
    {
        RuleFor(x => x.MaterialCode)
            .NotEmpty().WithMessage("MaterialCode is required.")
            .MaximumLength(InventoryConstants.MaterialCodeMaxLength);

        RuleFor(x => x.LotCode)
            .NotEmpty().WithMessage("LotCode is required.")
            .MaximumLength(InventoryConstants.LotCodeMaxLength);

        RuleFor(x => x.ReceivedDate)
            .NotEmpty().WithMessage("ReceivedDate is required.");

        RuleFor(x => x.Notes)
            .MaximumLength(InventoryConstants.NotesMaxLength);
    }
}
