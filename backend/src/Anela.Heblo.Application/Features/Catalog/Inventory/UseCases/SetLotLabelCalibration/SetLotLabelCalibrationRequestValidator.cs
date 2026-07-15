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

        RuleFor(x => x.DriftDotsPer100Labels)
            .InclusiveBetween(LotLabelCalibration.MinDriftDotsPer100Labels, LotLabelCalibration.MaxDriftDotsPer100Labels)
            .WithMessage($"Drift correction must be between {LotLabelCalibration.MinDriftDotsPer100Labels} and {LotLabelCalibration.MaxDriftDotsPer100Labels} dots per 100 labels.");
    }
}
