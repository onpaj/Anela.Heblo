using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;

public class UpdateLotRequestValidator : AbstractValidator<UpdateLotRequest>
{
    public UpdateLotRequestValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id is required.");
        RuleFor(x => x.ReceivedDate).NotEmpty().WithMessage("ReceivedDate is required.");
        RuleFor(x => x.Notes).MaximumLength(InventoryConstants.NotesMaxLength);
    }
}
