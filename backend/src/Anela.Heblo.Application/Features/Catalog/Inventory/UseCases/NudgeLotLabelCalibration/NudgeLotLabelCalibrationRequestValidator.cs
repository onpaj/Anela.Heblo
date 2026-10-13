using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.NudgeLotLabelCalibration;

public class NudgeLotLabelCalibrationRequestValidator : AbstractValidator<NudgeLotLabelCalibrationRequest>
{
    public NudgeLotLabelCalibrationRequestValidator()
    {
        // Both enums start at 1, so IsInEnum also rejects an omitted field rather than
        // letting it default into a direction or speed the operator never chose.
        RuleFor(x => x.Direction)
            .IsInEnum()
            .WithMessage("Drift direction must be either Up or Down.");

        RuleFor(x => x.Speed)
            .IsInEnum()
            .WithMessage("Drift speed must be either Fast or Slow.");
    }
}
