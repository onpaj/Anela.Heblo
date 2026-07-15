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

        RuleFor(x => x.DriftDotsPer10Labels)
            .InclusiveBetween(LotLabelCalibration.MinDriftDotsPer10Labels, LotLabelCalibration.MaxDriftDotsPer10Labels)
            .WithMessage($"Drift correction must be between {LotLabelCalibration.MinDriftDotsPer10Labels} and {LotLabelCalibration.MaxDriftDotsPer10Labels} dots per 10 labels.");
    }
}
