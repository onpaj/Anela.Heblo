using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.SetLotLabelCalibration;

public class SetLotLabelCalibrationRequestValidator : AbstractValidator<SetLotLabelCalibrationRequest>
{
    public SetLotLabelCalibrationRequestValidator()
    {
        RuleFor(x => x.PitchDots)
            .InclusiveBetween(LotLabelCalibration.MinPitchDots, LotLabelCalibration.MaxPitchDots)
            .WithMessage($"Pitch must be between {LotLabelCalibration.MinPitchDots} and {LotLabelCalibration.MaxPitchDots} dots.");

        RuleFor(x => x.DriftEveryNLabels)
            .InclusiveBetween(LotLabelCalibration.MinDriftEveryNLabels, LotLabelCalibration.MaxDriftEveryNLabels)
            .WithMessage($"Drift correction must be between {LotLabelCalibration.MinDriftEveryNLabels} and {LotLabelCalibration.MaxDriftEveryNLabels}.");
    }
}
