using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftSettings;

public class SetGiftSettingValidatorTests
{
    private readonly SetGiftSettingValidator _validator = new();

    [Fact]
    public void Validator_Passes_WhenDisabled()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_Passes_WhenEnabledWithValidValues()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = "DÁREK ZDARMA",
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_Fails_WhenEnabledWithZeroThreshold()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
        });
        result.ShouldHaveValidationErrorFor(x => x.ThresholdCzk);
    }

    [Fact]
    public void Validator_Fails_WhenEnabledWithEmptyText()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = string.Empty,
        });
        result.ShouldHaveValidationErrorFor(x => x.Text);
    }

    [Fact]
    public void Validator_Fails_WhenTextExceeds50Chars_EvenWhenDisabled()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = new string('X', 51),
        });
        result.ShouldHaveValidationErrorFor(x => x.Text);
    }
}
