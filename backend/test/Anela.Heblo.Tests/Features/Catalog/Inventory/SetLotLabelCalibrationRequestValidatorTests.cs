using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.SetLotLabelCalibration;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class SetLotLabelCalibrationRequestValidatorTests
{
    private readonly SetLotLabelCalibrationRequestValidator _validator = new();

    [Theory]
    [InlineData(LotLabelCalibration.MinPitchDots)]
    [InlineData(148)]
    [InlineData(LotLabelCalibration.MaxPitchDots)]
    public void Valid_Pitch_Passes(int pitch)
    {
        var result = _validator.TestValidate(
            new SetLotLabelCalibrationRequest { PitchDots = pitch, DriftDotsPer100Labels = 3 });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(LotLabelCalibration.MinPitchDots - 1)]
    [InlineData(LotLabelCalibration.MaxPitchDots + 1)]
    [InlineData(0)]
    public void Invalid_Pitch_Fails(int pitch)
    {
        var result = _validator.TestValidate(
            new SetLotLabelCalibrationRequest { PitchDots = pitch, DriftDotsPer100Labels = 3 });
        result.ShouldHaveValidationErrorFor(x => x.PitchDots);
    }

    [Theory]
    [InlineData(LotLabelCalibration.MinDriftDotsPer100Labels)] // 0 = disabled, valid
    [InlineData(3)]
    [InlineData(LotLabelCalibration.MaxDriftDotsPer100Labels)]
    public void Valid_Drift_Passes(int drift)
    {
        var result = _validator.TestValidate(
            new SetLotLabelCalibrationRequest { PitchDots = 148, DriftDotsPer100Labels = drift });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(LotLabelCalibration.MaxDriftDotsPer100Labels + 1)]
    public void Invalid_Drift_Fails(int drift)
    {
        var result = _validator.TestValidate(
            new SetLotLabelCalibrationRequest { PitchDots = 148, DriftDotsPer100Labels = drift });
        result.ShouldHaveValidationErrorFor(x => x.DriftDotsPer100Labels);
    }
}
