using FluentValidation;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingValidator : AbstractValidator<SetGiftSettingCommand>
{
    public SetGiftSettingValidator()
    {
        RuleFor(x => x.Text).MaximumLength(50);

        When(x => x.IsEnabled, () =>
        {
            RuleFor(x => x.ThresholdCzk).GreaterThan(0);
            RuleFor(x => x.Text).NotEmpty();
        });
    }
}
