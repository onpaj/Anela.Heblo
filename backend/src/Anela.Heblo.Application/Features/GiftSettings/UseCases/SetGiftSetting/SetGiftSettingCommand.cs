using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public sealed class SetGiftSettingCommand : IRequest<SetGiftSettingResponse>
{
    public bool IsEnabled { get; set; }
    public decimal ThresholdCzk { get; set; }
    public string Text { get; set; } = string.Empty;
}
